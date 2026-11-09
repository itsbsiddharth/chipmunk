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
    /// Reads conversations that coding agents keep on this PC: Codex (~/.codex/sessions) and
    /// Antigravity (~/.gemini/antigravity/brain). Listing only reads the start of each file and
    /// is cached, so it stays fast even with hundreds of megabytes of sessions.
    /// </summary>
    static class LocalAgents
    {
        class Entry { public string Id, Title, Sub; public DateTime Updated, Mtime; public long Size; public bool Skip; }
        static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        static readonly object Gate = new object();

        static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }

        static DateTime ParseTime(string s)
        {
            DateTime t;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t) ? t.ToLocalTime() : DateTime.MinValue;
        }

        static object TryParse(string line)
        {
            try { return Json.Parse(line); } catch { return null; }
        }

        static List<Dictionary<string, object>> List(IEnumerable<string> files, Func<string, Entry> index)
        {
            var list = new List<Entry>();
            lock (Gate)
            {
                foreach (var f in files)
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        Entry e;
                        if (!Cache.TryGetValue(f, out e) || e.Mtime != fi.LastWriteTimeUtc || e.Size != fi.Length)
                        {
                            e = index(f) ?? new Entry { Skip = true };
                            e.Mtime = fi.LastWriteTimeUtc;
                            e.Size = fi.Length;
                            if (e.Updated == DateTime.MinValue) e.Updated = fi.LastWriteTime;
                            Cache[f] = e;
                        }
                        if (!e.Skip) list.Add(e);
                    }
                    catch (Exception ex) { Log.Error("History index " + f + ": " + ex.Message); }
                }
            }
            return list.OrderByDescending(e => e.Updated).Select(e => new Dictionary<string, object>
            {
                { "id", e.Id }, { "title", e.Title }, { "sub", e.Sub },
                { "updated", e.Updated.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) },
                { "turns", -1 }, { "images", -1 }, { "search", e.Title },
            }).ToList();
        }

        // ---------------- Codex ----------------

        static string CodexRoot
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("CODEX_HOME");
                return Path.Combine(string.IsNullOrEmpty(env) ? Path.Combine(Home, ".codex") : env, "sessions");
            }
        }

        public static bool HasCodex { get { return Directory.Exists(CodexRoot); } }

        public static List<Dictionary<string, object>> CodexSessions()
        {
            if (!HasCodex) return new List<Dictionary<string, object>>();
            return List(Directory.EnumerateFiles(CodexRoot, "*.jsonl", SearchOption.AllDirectories), IndexCodex);
        }

        static Entry IndexCodex(string path)
        {
            string cwd = null;
            using (var sr = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8))
            {
                string line;
                int n = 0;
                while ((line = sr.ReadLine()) != null && n++ < 4000)
                {
                    if (line.IndexOf("\"session_meta\"", StringComparison.Ordinal) >= 0)
                    {
                        var mo = Json.Obj(TryParse(line));
                        var p = Json.Obj(mo != null && mo.ContainsKey("payload") ? mo["payload"] : null);
                        // Sub-agent threads are Codex talking to itself; only show the user's own sessions.
                        if (p != null && p.ContainsKey("source") && Json.Obj(p["source"]) != null && Json.Obj(p["source"]).ContainsKey("subagent")) return null;
                        cwd = Json.Str(p, "cwd");
                        continue;
                    }
                    if (line.IndexOf("\"role\":\"user\"", StringComparison.Ordinal) < 0) continue;
                    var turn = CodexUserTurn(Json.Obj(TryParse(line)));
                    if (turn == null || turn.Text.Length == 0) continue;
                    return new Entry
                    {
                        Id = path,
                        Title = Store.FirstLine(turn.Text, 90),
                        Sub = cwd != null ? Path.GetFileName(cwd.TrimEnd('\\', '/')) : "Codex",
                    };
                }
            }
            return null;
        }

        /// <summary>A user message as the person typed it, without the context Codex injects
        /// (environment, AGENTS.md, tool notes are all wrapped in tags or headed "# AGENTS.md").</summary>
        static HTurn CodexUserTurn(Dictionary<string, object> o)
        {
            if (o == null || Json.Str(o, "type") != "response_item") return null;
            var p = Json.Obj(o["payload"]);
            if (p == null || Json.Str(p, "type") != "message" || Json.Str(p, "role") != "user") return null;
            var turn = new HTurn { Role = "user", Time = ParseTime(Json.Str(o, "timestamp")) };
            var sb = new StringBuilder();
            foreach (var c in (Json.Arr(p.ContainsKey("content") ? p["content"] : null) ?? new object[0]).Select(Json.Obj).Where(x => x != null))
            {
                string type = Json.Str(c, "type");
                if (type == "input_text")
                {
                    string t = (Json.Str(c, "text") ?? "").Trim();
                    if (t.Length == 0 || t.StartsWith("<") || t.StartsWith("# AGENTS.md")) continue;
                    sb.Append(sb.Length > 0 ? "\n\n" : "").Append(t);
                }
                else if (type == "input_image")
                {
                    string url = Json.Str(c, "image_url") ?? "";
                    int comma = url.IndexOf(',');
                    int semi = url.IndexOf(';');
                    if (url.StartsWith("data:") && comma > 0)
                        turn.Images.Add(new HImage { Mime = semi > 5 ? url.Substring(5, semi - 5) : "image/png", Base64 = url.Substring(comma + 1) });
                }
            }
            turn.Text = sb.ToString();
            return turn.Text.Length == 0 && turn.Images.Count == 0 ? null : turn;
        }

        public static List<HTurn> CodexTurns(string path, out string title)
        {
            title = null;
            var turns = new List<HTurn>();
            var seen = new HashSet<string>();
            using (var sr = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    bool user = line.IndexOf("\"role\":\"user\"", StringComparison.Ordinal) >= 0;
                    bool assistant = !user && line.IndexOf("\"role\":\"assistant\"", StringComparison.Ordinal) >= 0 && line.IndexOf("\"output_text\"", StringComparison.Ordinal) >= 0;
                    if (!user && !assistant) continue;
                    var o = Json.Obj(TryParse(line));
                    var p = Json.Obj(o != null && o.ContainsKey("payload") ? o["payload"] : null);
                    string id = Json.Str(p, "id");
                    if (id != null && !seen.Add(id)) continue;
                    if (user)
                    {
                        var t = CodexUserTurn(o);
                        if (t != null) turns.Add(t);
                        continue;
                    }
                    if (Json.Str(p, "type") != "message") continue;
                    var sb = new StringBuilder();
                    foreach (var c in (Json.Arr(p.ContainsKey("content") ? p["content"] : null) ?? new object[0]).Select(Json.Obj).Where(x => x != null))
                        if (Json.Str(c, "type") == "output_text") sb.Append(Json.Str(c, "text")).Append("\n\n");
                    string text = sb.ToString().Trim();
                    if (text.Length == 0) continue;
                    var last = turns.Count > 0 ? turns[turns.Count - 1] : null;
                    if (last != null && last.Role == "assistant") last.Text += "\n\n" + text;
                    else turns.Add(new HTurn { Role = "assistant", Text = text, Time = ParseTime(Json.Str(o, "timestamp")) });
                }
            }
            var first = turns.FirstOrDefault(x => x.Role == "user" && x.Text.Length > 0);
            title = first != null ? Store.FirstLine(first.Text, 90) : "Codex session";
            return turns;
        }

        // ---------------- Antigravity ----------------

        static string AntigravityRoot { get { return Path.Combine(Home, ".gemini", "antigravity", "brain"); } }

        public static bool HasAntigravity { get { return Directory.Exists(AntigravityRoot); } }

        static string Transcript(string convDir) { return Path.Combine(convDir, ".system_generated", "logs", "transcript.jsonl"); }

        public static List<Dictionary<string, object>> AntigravitySessions()
        {
            if (!HasAntigravity) return new List<Dictionary<string, object>>();
            var files = Directory.GetDirectories(AntigravityRoot).Select(Transcript).Where(File.Exists);
            return List(files, f =>
            {
                string title;
                var turns = AntigravityTurnsFromFile(f, out title, false);
                if (!turns.Any(t => t.Role == "user")) return null;
                // the conversation folder, two levels above logs\transcript.jsonl
                string conv = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(f)));
                return new Entry { Id = conv, Title = title, Sub = "Antigravity", Updated = turns.Max(t => t.Time) };
            });
        }

        static readonly Regex UserRequest = new Regex(@"<USER_REQUEST>([\s\S]*?)</USER_REQUEST>", RegexOptions.Compiled);
        static readonly Regex AnyBlock = new Regex(@"<([A-Z_]+)>[\s\S]*?</\1>", RegexOptions.Compiled);

        public static List<HTurn> AntigravityTurns(string convDir, out string title)
        {
            return AntigravityTurnsFromFile(Transcript(convDir), out title, true);
        }

        static List<HTurn> AntigravityTurnsFromFile(string transcript, out string title, bool withImages)
        {
            var turns = new List<HTurn>();
            foreach (var line in File.ReadLines(transcript))
            {
                var o = Json.Obj(TryParse(line));
                if (o == null) continue;
                string type = Json.Str(o, "type");
                string content = Json.Str(o, "content");
                if (string.IsNullOrWhiteSpace(content)) continue;
                var time = ParseTime(Json.Str(o, "created_at"));
                if (type == "USER_INPUT")
                {
                    var m = UserRequest.Match(content);
                    string text = (m.Success ? m.Groups[1].Value : AnyBlock.Replace(content, "")).Trim();
                    if (text.Length > 0) turns.Add(new HTurn { Role = "user", Text = text, Time = time });
                }
                else if (type == "PLANNER_RESPONSE")
                {
                    var last = turns.Count > 0 ? turns[turns.Count - 1] : null;
                    if (last != null && last.Role == "assistant") last.Text += "\n\n" + content.Trim();
                    else turns.Add(new HTurn { Role = "assistant", Text = content.Trim(), Time = time });
                }
            }
            if (withImages) AttachUploads(transcript, turns);
            var first = turns.FirstOrDefault(t => t.Role == "user");
            title = first != null ? Store.FirstLine(first.Text, 90) : "Antigravity conversation";
            return turns;
        }

        /// <summary>Uploaded images are saved as media__&lt;unix ms&gt;.png; each belongs to the first
        /// message sent after it was uploaded.</summary>
        static void AttachUploads(string transcript, List<HTurn> turns)
        {
            string conv = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(transcript)));
            string uploads = Path.Combine(conv, ".user_uploaded");
            var users = turns.Where(t => t.Role == "user").ToList();
            if (!Directory.Exists(uploads) || users.Count == 0) return;
            foreach (var f in Directory.GetFiles(uploads).OrderBy(x => x))
            {
                var m = Regex.Match(Path.GetFileName(f), @"(\d{12,14})");
                DateTime at = m.Success ? DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(m.Groups[1].Value)).LocalDateTime : File.GetLastWriteTime(f);
                var owner = users.FirstOrDefault(t => t.Time >= at.AddSeconds(-2)) ?? users[users.Count - 1];
                owner.Images.Add(new HImage { Path = f });
            }
        }
    }
}
