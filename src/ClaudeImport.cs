using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Chipmunk
{
    /// <summary>
    /// Reads Claude Code session transcripts (~/.claude*/projects/*/*.jsonl) so past prompts,
    /// including the screenshots pasted into them, can be reopened as drafts.
    /// </summary>
    static class ClaudeImport
    {
        public class Prompt
        {
            public int Index;
            public DateTime Time;
            public string Text = "";
            public List<KeyValuePair<string, string>> Images = new List<KeyValuePair<string, string>>(); // media type, base64
            public int ImageCount;
        }

        class SessionInfo
        {
            public string Path, Title, Project, Search;
            public DateTime Updated, Mtime;
            public long Size;
            public int PromptCount, ImageCount;
        }

        static readonly Dictionary<string, SessionInfo> Cache = new Dictionary<string, SessionInfo>(StringComparer.OrdinalIgnoreCase);
        static readonly object Gate = new object();
        static readonly Regex Reminder = new Regex(@"<system-reminder>[\s\S]*?</system-reminder>", RegexOptions.Compiled);
        static readonly Regex PastedTag = new Regex(@"</?pasted_content\b[^>]*>", RegexOptions.Compiled);
        static readonly string[] NoisePrefixes = { "<command-name>", "<command-message>", "<local-command", "Caveat:", "[Request interrupted", "<task-notification>", "<bash-input>", "<bash-stdout>", "<user-memory-input>" };

        static IEnumerable<string> Roots()
        {
            var roots = new List<string>();
            string env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (!string.IsNullOrEmpty(env)) roots.Add(Path.Combine(env, "projects"));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            try
            {
                foreach (var d in Directory.GetDirectories(home, ".claude*")) roots.Add(Path.Combine(d, "projects"));
            }
            catch { }
            return roots.Where(Directory.Exists).Select(p => Path.GetFullPath(p).TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        static IEnumerable<string> TranscriptFiles()
        {
            foreach (var root in Roots())
            {
                string[] projects;
                try { projects = Directory.GetDirectories(root); } catch { continue; }
                foreach (var p in projects)
                {
                    string[] files;
                    try { files = Directory.GetFiles(p, "*.jsonl", SearchOption.TopDirectoryOnly); } catch { continue; }
                    foreach (var f in files) yield return f;
                }
            }
        }

        public static List<Dictionary<string, object>> Sessions()
        {
            var list = new List<SessionInfo>();
            lock (Gate)
            {
                foreach (var f in TranscriptFiles())
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        SessionInfo s;
                        if (!Cache.TryGetValue(f, out s) || s.Mtime != fi.LastWriteTimeUtc || s.Size != fi.Length)
                        {
                            s = Index(fi);
                            Cache[f] = s;
                        }
                        if (s != null && s.PromptCount > 0) list.Add(s);
                    }
                    catch (Exception e) { Log.Error("Import index " + f + ": " + e.Message); }
                }
            }
            return list.OrderByDescending(s => s.Updated).Select(s => new Dictionary<string, object>
            {
                { "path", s.Path }, { "title", s.Title }, { "project", s.Project },
                { "updated", s.Updated.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) },
                { "promptCount", s.PromptCount }, { "imageCount", s.ImageCount }, { "search", s.Search },
            }).ToList();
        }

        static SessionInfo Index(FileInfo fi)
        {
            string title, cwd;
            var prompts = Read(fi.FullName, false, out title, out cwd);
            if (prompts.Count == 0) return new SessionInfo { Path = fi.FullName, Mtime = fi.LastWriteTimeUtc, Size = fi.Length };
            var s = new SessionInfo
            {
                Path = fi.FullName,
                Mtime = fi.LastWriteTimeUtc,
                Size = fi.Length,
                Updated = prompts.Max(p => p.Time == DateTime.MinValue ? fi.LastWriteTime : p.Time),
                PromptCount = prompts.Count,
                ImageCount = prompts.Sum(p => p.ImageCount),
                Project = cwd != null ? Path.GetFileName(cwd.TrimEnd('\\', '/')) : fi.Directory.Name,
            };
            s.Title = !string.IsNullOrWhiteSpace(title) ? title.Trim() : Store.FirstLine(prompts[0].Text, 90);
            if (string.IsNullOrWhiteSpace(s.Title)) s.Title = "Untitled session";
            var sb = new StringBuilder();
            foreach (var p in prompts) sb.Append(p.Text.Length > 400 ? p.Text.Substring(0, 400) : p.Text).Append(' ');
            s.Search = sb.ToString();
            return s;
        }

        static readonly Regex Mention = new Regex(@"@""([^""\r\n]+)""|@([A-Za-z]:\\[^\s""]+)", RegexOptions.Compiled);

        /// <summary>
        /// Finds files the prompt pulled in with @"path" mentions. Returns the ones that still exist,
        /// and the prompt text with those mentions removed (the files become attachments instead).
        /// </summary>
        public static List<string> ReferencedFiles(string text, string cwd, out string remaining)
        {
            var found = new List<string>();
            remaining = Mention.Replace(text ?? "", m =>
            {
                string raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                string full = raw;
                try
                {
                    if (!Path.IsPathRooted(full) && cwd != null) full = Path.GetFullPath(Path.Combine(cwd, raw));
                }
                catch { return m.Value; }
                if (!File.Exists(full) || new FileInfo(full).Length > 100L * 1024 * 1024) return m.Value;
                if (!found.Contains(full, StringComparer.OrdinalIgnoreCase)) found.Add(full);
                return "";
            });
            remaining = Regex.Replace(remaining, @"[ \t]+\r?\n", "\n");
            remaining = Regex.Replace(remaining, @"\n{3,}", "\n\n").Trim();
            return found;
        }

        static List<Prompt> Read(string path, bool withImages, out string title, out string cwd)
        {
            title = null;
            cwd = null;
            var prompts = new List<Prompt>();
            var seen = new HashSet<string>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Length < 20) continue;
                    if (line.IndexOf("\"type\":\"custom-title\"", StringComparison.Ordinal) >= 0)
                    {
                        var o = Json.Obj(TryParse(line));
                        string t = Json.Str(o, "customTitle");
                        if (!string.IsNullOrWhiteSpace(t)) title = t;
                        continue;
                    }
                    if (line.IndexOf("\"type\":\"user\"", StringComparison.Ordinal) < 0) continue;
                    if (line.IndexOf("\"tool_use_id\"", StringComparison.Ordinal) >= 0) continue;
                    var obj = Json.Obj(TryParse(line));
                    if (obj == null || Json.Str(obj, "type") != "user") continue;
                    var p = ToPrompt(obj, withImages);
                    if (p == null) continue;
                    string uuid = Json.Str(obj, "uuid");
                    if (uuid != null && !seen.Add(uuid)) continue;
                    p.Index = prompts.Count;
                    prompts.Add(p);
                    if (cwd == null) cwd = Json.Str(obj, "cwd");
                }
            }
            return prompts;
        }

        /// <summary>The folder the session ran in, for resolving @file mentions.</summary>
        public static string Cwd(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    for (int i = 0; i < 400 && (line = sr.ReadLine()) != null; i++)
                    {
                        if (line.IndexOf("\"cwd\"", StringComparison.Ordinal) < 0) continue;
                        string cwd = Json.Str(Json.Obj(TryParse(line)), "cwd");
                        if (!string.IsNullOrEmpty(cwd)) return cwd;
                    }
                }
            }
            catch { }
            return null;
        }

        static object TryParse(string line)
        {
            try { return Json.Parse(line); } catch { return null; }
        }

        /// <summary>The whole conversation: your prompts (with their images) and Claude's written replies.</summary>
        public static List<HTurn> Turns(string path, out string title)
        {
            title = null;
            var turns = new List<HTurn>();
            var seen = new HashSet<string>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Length < 20) continue;
                    if (line.IndexOf("\"type\":\"custom-title\"", StringComparison.Ordinal) >= 0)
                    {
                        string t = Json.Str(Json.Obj(TryParse(line)), "customTitle");
                        if (!string.IsNullOrWhiteSpace(t)) title = t;
                        continue;
                    }
                    bool user = line.IndexOf("\"type\":\"user\"", StringComparison.Ordinal) >= 0 && line.IndexOf("\"tool_use_id\"", StringComparison.Ordinal) < 0;
                    bool assistant = !user && line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) >= 0 && line.IndexOf("\"type\":\"text\"", StringComparison.Ordinal) >= 0;
                    if (!user && !assistant) continue;
                    var obj = Json.Obj(TryParse(line));
                    if (obj == null || Json.Bool(obj, "isSidechain")) continue;
                    string uuid = Json.Str(obj, "uuid");
                    if (uuid != null && !seen.Add(uuid)) continue;
                    if (user && Json.Str(obj, "type") == "user")
                    {
                        var p = ToPrompt(obj, true);
                        if (p == null) continue;
                        var turn = new HTurn { Role = "user", Text = p.Text, Time = p.Time };
                        foreach (var img in p.Images) turn.Images.Add(new HImage { Mime = img.Key, Base64 = img.Value });
                        turns.Add(turn);
                    }
                    else if (Json.Str(obj, "type") == "assistant")
                    {
                        var msg = Json.Obj(obj.ContainsKey("message") ? obj["message"] : null);
                        var parts = Json.Arr(msg != null && msg.ContainsKey("content") ? msg["content"] : null);
                        if (parts == null) continue;
                        var sb = new StringBuilder();
                        foreach (var part in parts.Select(Json.Obj))
                            if (Json.Str(part, "type") == "text") sb.Append(Json.Str(part, "text")).Append("\n\n");
                        string text = sb.ToString().Trim();
                        if (text.Length == 0) continue;
                        // One reply often spans several lines in the transcript; keep it as one turn.
                        var last = turns.Count > 0 ? turns[turns.Count - 1] : null;
                        if (last != null && last.Role == "assistant") last.Text += "\n\n" + text;
                        else
                        {
                            DateTime t;
                            DateTime.TryParse(Json.Str(obj, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t);
                            turns.Add(new HTurn { Role = "assistant", Text = text, Time = t == DateTime.MinValue ? t : t.ToLocalTime() });
                        }
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                var first = turns.FirstOrDefault(x => x.Role == "user");
                title = first != null ? Store.FirstLine(first.Text, 80) : "Claude Code session";
            }
            return turns;
        }

        static Prompt ToPrompt(Dictionary<string, object> o, bool withImages)
        {
            if (Json.Bool(o, "isMeta") || Json.Bool(o, "isSidechain") || Json.Bool(o, "isCompactSummary")) return null;
            var origin = Json.Obj(o.ContainsKey("origin") ? o["origin"] : null);
            string kind = Json.Str(origin, "kind");
            if (kind != null && kind != "human") return null;
            var msg = Json.Obj(o.ContainsKey("message") ? o["message"] : null);
            if (msg == null || !msg.ContainsKey("content")) return null;

            var p = new Prompt();
            var sb = new StringBuilder();
            object content = msg["content"];
            if (content is string s) sb.Append(s);
            else
            {
                var arr = Json.Arr(content);
                if (arr == null) return null;
                foreach (var item in arr)
                {
                    var part = Json.Obj(item);
                    string type = Json.Str(part, "type");
                    if (type == "tool_result") return null;
                    if (type == "text")
                    {
                        if (sb.Length > 0) sb.Append("\n\n");
                        sb.Append(Json.Str(part, "text"));
                    }
                    else if (type == "image")
                    {
                        p.ImageCount++;
                        var src = Json.Obj(part.ContainsKey("source") ? part["source"] : null);
                        if (withImages && Json.Str(src, "type") == "base64")
                            p.Images.Add(new KeyValuePair<string, string>(Json.Str(src, "media_type"), Json.Str(src, "data")));
                    }
                }
            }
            string text = PastedTag.Replace(Reminder.Replace(sb.ToString(), ""), "");
            text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
            if (text.Length == 0 && p.ImageCount == 0) return null;
            if (p.ImageCount == 0 && NoisePrefixes.Any(n => text.StartsWith(n, StringComparison.Ordinal))) return null;
            p.Text = text;
            DateTime t;
            if (DateTime.TryParse(Json.Str(o, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t))
                p.Time = t.ToLocalTime();
            return p;
        }
    }
}
