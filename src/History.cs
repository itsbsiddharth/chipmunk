using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Chipmunk
{
    class HImage
    {
        public string Mime;
        public string Base64;  // Claude Code transcripts carry images inline
        public string Path;    // saved chats keep them as files
    }

    class HTurn
    {
        public string Role;    // "user" or "assistant"
        public string Text = "";
        public DateTime Time;
        public List<HImage> Images = new List<HImage>();
    }

    /// <summary>
    /// Past chats you can reuse: Claude Code sessions (read live from disk), plus ChatGPT, Claude
    /// and Gemini conversations saved from the browser extension or imported from a data export.
    /// Saved chats live in LocalDir\history\&lt;source&gt;\&lt;id&gt;\session.json with their images.
    /// </summary>
    static class History
    {
        public static readonly string[] Sources = { "claude-code", "codex", "antigravity", "chatgpt", "claude", "gemini" };

        /// <summary>Sources read straight from the agent's own files on this PC (nothing to import).</summary>
        static bool IsLocal(string s) { return s == "claude-code" || s == "codex" || s == "antigravity"; }

        public static string SourceName(string s)
        {
            switch (s)
            {
                case "claude-code": return "Claude Code";
                case "codex": return "Codex";
                case "antigravity": return "Antigravity";
                case "chatgpt": return "ChatGPT";
                case "claude": return "Claude";
                case "gemini": return "Gemini";
                default: return "Saved chats";
            }
        }

        static string Root { get { return Path.Combine(Settings.LocalDir, "history"); } }
        static string SourceDir(string source) { return Path.Combine(Root, SafeName(source)); }

        static string SafeName(string s)
        {
            s = Regex.Replace(s ?? "", @"[^A-Za-z0-9_\-]", "_");
            return s.Length == 0 ? "x" : (s.Length > 80 ? s.Substring(0, 80) : s);
        }

        // ---------------- listing ----------------

        public static List<Dictionary<string, object>> Summary()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var s in Sources)
            {
                if (s == "codex" && !LocalAgents.HasCodex) continue;
                if (s == "antigravity" && !LocalAgents.HasAntigravity) continue;
                int count = IsLocal(s) ? -1 : ReadIndex(s).Count;
                list.Add(new Dictionary<string, object> { { "id", s }, { "name", SourceName(s) }, { "count", count }, { "local", IsLocal(s) } });
            }
            return list;
        }

        public static List<Dictionary<string, object>> Sessions(string source)
        {
            if (source == "codex") return LocalAgents.CodexSessions();
            if (source == "antigravity") return LocalAgents.AntigravitySessions();
            if (source == "claude-code")
            {
                return ClaudeImport.Sessions().Select(x => new Dictionary<string, object>
                {
                    { "id", x["path"] }, { "title", x["title"] }, { "sub", x["project"] }, { "updated", x["updated"] },
                    { "turns", x["promptCount"] }, { "images", x["imageCount"] }, { "search", x["search"] },
                }).ToList();
            }
            return ReadIndex(source).OrderByDescending(x => Json.Str(x, "updated"), StringComparer.Ordinal).ToList();
        }

        static List<Dictionary<string, object>> ReadIndex(string source)
        {
            string f = Path.Combine(SourceDir(source), "index.json");
            try
            {
                if (File.Exists(f)) return (Json.Arr(Json.Parse(File.ReadAllText(f))) ?? new object[0]).Select(Json.Obj).Where(x => x != null).ToList();
            }
            catch (Exception e) { Log.Error("History index " + source + ": " + e.Message); }
            return new List<Dictionary<string, object>>();
        }

        static void WriteIndex(string source, List<Dictionary<string, object>> items)
        {
            Json.WriteAtomic(Path.Combine(SourceDir(source), "index.json"), Json.Serialize(items));
        }

        // ---------------- reading one conversation ----------------

        public static List<HTurn> Load(string source, string id, out string title, out string url)
        {
            url = null;
            if (source == "claude-code" || source == "codex")
            {
                if (!File.Exists(id)) throw new InvalidOperationException("That session is no longer on this PC.");
                return source == "codex" ? LocalAgents.CodexTurns(id, out title) : ClaudeImport.Turns(id, out title);
            }
            if (source == "antigravity")
            {
                if (!Directory.Exists(id)) throw new InvalidOperationException("That Antigravity conversation is no longer on this PC.");
                return LocalAgents.AntigravityTurns(id, out title);
            }
            string dir = Path.Combine(SourceDir(source), SafeName(id));
            string f = Path.Combine(dir, "session.json");
            if (!File.Exists(f)) throw new InvalidOperationException("That chat is no longer saved.");
            var o = Json.Obj(Json.Parse(File.ReadAllText(f)));
            title = Json.Str(o, "title");
            url = Json.Str(o, "url");
            var turns = new List<HTurn>();
            foreach (var t in (Json.Arr(o["turns"]) ?? new object[0]).Select(Json.Obj))
            {
                var turn = new HTurn { Role = Json.Str(t, "role") == "assistant" ? "assistant" : "user", Text = Json.Str(t, "text") ?? "" };
                DateTime when;
                if (DateTime.TryParse(Json.Str(t, "time"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out when)) turn.Time = when.ToLocalTime();
                foreach (var img in (Json.Arr(t.ContainsKey("images") ? t["images"] : null) ?? new object[0]))
                    turn.Images.Add(new HImage { Path = Path.Combine(dir, Path.GetFileName(img as string ?? "")) });
                turns.Add(turn);
            }
            return turns;
        }

        public static byte[] ImageBytes(HImage img)
        {
            if (img.Base64 != null) return Convert.FromBase64String(img.Base64);
            return File.ReadAllBytes(img.Path);
        }

        /// <summary>What the UI shows for a conversation: every turn, with small thumbnails.</summary>
        public static Dictionary<string, object> Conversation(string source, string id)
        {
            string title, url;
            var turns = Load(source, id, out title, out url);
            return new Dictionary<string, object>
            {
                { "source", source }, { "sourceName", SourceName(source) }, { "id", id }, { "title", title }, { "url", url },
                { "turns", turns.Select((t, i) => new Dictionary<string, object>
                    {
                        { "index", i }, { "role", t.Role },
                        { "time", t.Time == DateTime.MinValue ? null : t.Time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) },
                        { "text", t.Text.Length > 20000 ? t.Text.Substring(0, 20000) + "\n…" : t.Text },
                        { "images", t.Images.Select((img, j) =>
                            {
                                string thumb = null;
                                try { thumb = ImageUtil.ThumbDataUrl(ImageBytes(img), 240, 160); } catch { }
                                return (object)new Dictionary<string, object> { { "index", j }, { "thumb", thumb } };
                            }).ToList() },
                    }).ToList() },
            };
        }

        // ---------------- saving ----------------

        /// <summary>Saves a chat (from the extension or an export). Returns its id.</summary>
        public static string Save(string source, string id, string title, string url, DateTime updated, List<HTurn> turns, Func<HImage, byte[]> bytesOf)
        {
            source = SafeName(source);
            if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");
            string dir = Path.Combine(SourceDir(source), SafeName(id));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var turnList = new List<object>();
            int n = 0;
            foreach (var t in turns)
            {
                var names = new List<string>();
                foreach (var img in t.Images)
                {
                    byte[] data;
                    try { data = bytesOf(img); } catch { continue; }
                    if (data == null || data.Length == 0) continue;
                    string name = "img-" + (++n).ToString("000") + ImageUtil.ExtensionFor(img.Mime, data);
                    File.WriteAllBytes(Path.Combine(dir, name), data);
                    names.Add(name);
                }
                turnList.Add(new Dictionary<string, object>
                {
                    { "role", t.Role }, { "text", t.Text ?? "" }, { "images", names },
                    { "time", t.Time == DateTime.MinValue ? null : t.Time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) },
                });
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                var first = turns.FirstOrDefault(x => x.Role == "user");
                title = first != null ? Store.FirstLine(first.Text, 80) : "Untitled chat";
            }
            string updatedIso = (updated == DateTime.MinValue ? DateTime.UtcNow : updated.ToUniversalTime()).ToString("o", CultureInfo.InvariantCulture);
            Json.WriteAtomic(Path.Combine(dir, "session.json"), Json.Serialize(new Dictionary<string, object>
            {
                { "id", id }, { "source", source }, { "title", title }, { "url", url }, { "updated", updatedIso }, { "turns", turnList },
            }));

            var index = ReadIndex(source).Where(x => Json.Str(x, "id") != id).ToList();
            var firstUser = turns.FirstOrDefault(x => x.Role == "user");
            index.Add(new Dictionary<string, object>
            {
                { "id", id }, { "title", title }, { "sub", url != null ? Host(url) : SourceName(source) }, { "updated", updatedIso },
                { "turns", turns.Count(x => x.Role == "user") }, { "images", n },
                { "search", (firstUser != null ? firstUser.Text : "").Length > 400 ? firstUser.Text.Substring(0, 400) : (firstUser != null ? firstUser.Text : "") },
            });
            WriteIndex(source, index);
            return id;
        }

        public static void Delete(string source, string id)
        {
            if (IsLocal(source)) return;
            string dir = Path.Combine(SourceDir(source), SafeName(id));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            WriteIndex(source, ReadIndex(source).Where(x => Json.Str(x, "id") != id).ToList());
        }

        static string Host(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host.Replace("www.", "") : url;
        }

        /// <summary>A chat the browser extension read from chatgpt.com, claude.ai or gemini.google.com.</summary>
        public static Dictionary<string, object> SaveFromBrowser(Dictionary<string, object> s)
        {
            string url = Json.Str(s, "url");
            string source = Json.Str(s, "source");
            if (Array.IndexOf(Sources, source) < 0 || IsLocal(source)) source = "chatgpt";
            var turns = new List<HTurn>();
            foreach (var t in (Json.Arr(s.ContainsKey("turns") ? s["turns"] : null) ?? new object[0]).Select(Json.Obj).Where(x => x != null))
            {
                var turn = new HTurn { Role = Json.Str(t, "role") == "assistant" ? "assistant" : "user", Text = (Json.Str(t, "text") ?? "").Trim() };
                foreach (var img in (Json.Arr(t.ContainsKey("images") ? t["images"] : null) ?? new object[0]))
                {
                    string data = img as string;
                    if (string.IsNullOrEmpty(data) || !data.StartsWith("data:")) continue;
                    int comma = data.IndexOf(',');
                    int semi = data.IndexOf(';');
                    turn.Images.Add(new HImage { Mime = semi > 5 ? data.Substring(5, semi - 5) : "image/png", Base64 = data.Substring(comma + 1) });
                }
                if (turn.Text.Length > 0 || turn.Images.Count > 0) turns.Add(turn);
            }
            if (turns.Count == 0) throw new InvalidOperationException("Couldn't find any messages on that page.");
            // The URL identifies the conversation, so saving it again updates the same entry.
            string id = url != null ? Regex.Match(url, @"/(c|chat|app)/([A-Za-z0-9\-_]+)").Groups[2].Value : null;
            if (string.IsNullOrEmpty(id)) id = SafeName(url ?? Guid.NewGuid().ToString("N"));
            Save(source, id, Json.Str(s, "title"), url, DateTime.UtcNow, turns, ImageBytes);
            return new Dictionary<string, object> { { "source", source }, { "id", id }, { "turns", turns.Count } };
        }

        // ---------------- data exports ----------------

        /// <summary>Imports a ChatGPT or Claude data export (.zip). Returns (source, conversations, images).</summary>
        public static Tuple<string, int, int> ImportExport(string zipPath, Action<string> progress)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var jsonEntries = zip.Entries.Where(e => Regex.IsMatch(e.Name, @"^conversations.*\.json$", RegexOptions.IgnoreCase)).ToList();
                if (jsonEntries.Count == 0) throw new InvalidOperationException("That zip has no conversations.json. Pick the export file ChatGPT or Claude emailed you.");
                // Index every file by name so image pointers can be matched however the export is laid out.
                var files = zip.Entries.Where(e => e.Length > 0 && !e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !e.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)).ToList();
                int convs = 0, images = 0;
                string source = null;
                foreach (var je in jsonEntries)
                {
                    string text;
                    using (var r = new StreamReader(je.Open(), Encoding.UTF8)) text = r.ReadToEnd();
                    var arr = Json.Arr(Json.Parse(text));
                    if (arr == null) continue;
                    foreach (var c in arr.Select(Json.Obj).Where(x => x != null))
                    {
                        bool isChatGpt = c.ContainsKey("mapping");
                        bool isClaude = c.ContainsKey("chat_messages");
                        if (!isChatGpt && !isClaude) continue;
                        source = isChatGpt ? "chatgpt" : "claude";
                        string title, id;
                        DateTime updated;
                        var turns = isChatGpt ? ChatGptTurns(c, out title, out id, out updated) : ClaudeTurns(c, out title, out id, out updated);
                        if (turns.Count == 0) continue;
                        string url = isChatGpt ? "https://chatgpt.com/c/" + id : "https://claude.ai/chat/" + id;
                        Save(source, id, title, url, updated, turns, img =>
                        {
                            var entry = FindFile(files, img.Path);
                            if (entry == null) return null;
                            using (var s = entry.Open())
                            using (var ms = new MemoryStream()) { s.CopyTo(ms); images++; return ms.ToArray(); }
                        });
                        convs++;
                        if (convs % 25 == 0 && progress != null) progress("Imported " + convs + " chats…");
                    }
                }
                if (source == null) throw new InvalidOperationException("Couldn't recognise that export. Chipmunk reads ChatGPT and Claude exports.");
                return Tuple.Create(source, convs, images);
            }
        }

        static ZipArchiveEntry FindFile(List<ZipArchiveEntry> files, string fileId)
        {
            if (string.IsNullOrEmpty(fileId)) return null;
            // Exports name files "<id>-<original name>" or "<id>.<ext>"; match the id exactly first.
            return files.FirstOrDefault(e => e.Name.StartsWith(fileId + "-", StringComparison.OrdinalIgnoreCase) || e.Name.StartsWith(fileId + ".", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileNameWithoutExtension(e.Name).Equals(fileId, StringComparison.OrdinalIgnoreCase))
                ?? files.FirstOrDefault(e => e.Name.StartsWith(fileId, StringComparison.OrdinalIgnoreCase))
                ?? files.FirstOrDefault(e => e.Name.IndexOf(fileId, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static DateTime FromUnix(object v)
        {
            try { return v == null ? DateTime.MinValue : DateTimeOffset.FromUnixTimeMilliseconds((long)(Convert.ToDouble(v) * 1000)).LocalDateTime; }
            catch { return DateTime.MinValue; }
        }

        /// <summary>ChatGPT stores a tree of messages; follow the branch the user last saw.</summary>
        static List<HTurn> ChatGptTurns(Dictionary<string, object> c, out string title, out string id, out DateTime updated)
        {
            title = Json.Str(c, "title");
            id = Json.Str(c, "conversation_id") ?? Json.Str(c, "id") ?? Guid.NewGuid().ToString("N");
            updated = FromUnix(c.ContainsKey("update_time") ? c["update_time"] : null);
            var mapping = Json.Obj(c["mapping"]);
            var turns = new List<HTurn>();
            if (mapping == null) return turns;
            var chain = new List<Dictionary<string, object>>();
            string node = Json.Str(c, "current_node");
            if (node == null || !mapping.ContainsKey(node))
                node = mapping.Keys.LastOrDefault(k => (Json.Arr(Json.Obj(mapping[k]).ContainsKey("children") ? Json.Obj(mapping[k])["children"] : null) ?? new object[0]).Length == 0);
            var guard = new HashSet<string>();
            while (node != null && mapping.ContainsKey(node) && guard.Add(node))
            {
                var n = Json.Obj(mapping[node]);
                chain.Add(n);
                node = Json.Str(n, "parent");
            }
            chain.Reverse();
            foreach (var n in chain)
            {
                var msg = Json.Obj(n.ContainsKey("message") ? n["message"] : null);
                if (msg == null) continue;
                string role = Json.Str(Json.Obj(msg.ContainsKey("author") ? msg["author"] : null), "role");
                if (role != "user" && role != "assistant") continue;
                var meta = Json.Obj(msg.ContainsKey("metadata") ? msg["metadata"] : null);
                if (Json.Bool(meta, "is_visually_hidden_from_conversation")) continue;
                var content = Json.Obj(msg.ContainsKey("content") ? msg["content"] : null);
                string ctype = Json.Str(content, "content_type");
                if (ctype != "text" && ctype != "multimodal_text") continue;
                var turn = new HTurn { Role = role, Time = FromUnix(msg.ContainsKey("create_time") ? msg["create_time"] : null) };
                var sb = new StringBuilder();
                foreach (var part in Json.Arr(content.ContainsKey("parts") ? content["parts"] : null) ?? new object[0])
                {
                    if (part is string s) { if (s.Length > 0) sb.Append(sb.Length > 0 ? "\n\n" : "").Append(s); continue; }
                    var p = Json.Obj(part);
                    if (Json.Str(p, "content_type") == "image_asset_pointer")
                    {
                        string pointer = Json.Str(p, "asset_pointer") ?? "";
                        int i = pointer.IndexOf("://", StringComparison.Ordinal);
                        turn.Images.Add(new HImage { Path = i >= 0 ? pointer.Substring(i + 3) : pointer });
                    }
                }
                turn.Text = sb.ToString().Trim();
                if (turn.Text.Length == 0 && turn.Images.Count == 0) continue;
                if (role == "assistant" && turns.Count > 0 && turns[turns.Count - 1].Role == "assistant") turns[turns.Count - 1].Text += "\n\n" + turn.Text;
                else turns.Add(turn);
            }
            return turns;
        }

        static List<HTurn> ClaudeTurns(Dictionary<string, object> c, out string title, out string id, out DateTime updated)
        {
            title = Json.Str(c, "name");
            id = Json.Str(c, "uuid") ?? Guid.NewGuid().ToString("N");
            DateTime.TryParse(Json.Str(c, "updated_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out updated);
            var turns = new List<HTurn>();
            foreach (var m in (Json.Arr(c["chat_messages"]) ?? new object[0]).Select(Json.Obj).Where(x => x != null))
            {
                var turn = new HTurn { Role = Json.Str(m, "sender") == "assistant" ? "assistant" : "user" };
                DateTime t;
                if (DateTime.TryParse(Json.Str(m, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) turn.Time = t.ToLocalTime();
                var sb = new StringBuilder(Json.Str(m, "text") ?? "");
                if (sb.Length == 0)
                    foreach (var part in (Json.Arr(m.ContainsKey("content") ? m["content"] : null) ?? new object[0]).Select(Json.Obj))
                        if (Json.Str(part, "type") == "text") sb.Append(Json.Str(part, "text")).Append("\n\n");
                // Attached documents come with their extracted text; keep it with the message.
                foreach (var a in (Json.Arr(m.ContainsKey("attachments") ? m["attachments"] : null) ?? new object[0]).Select(Json.Obj).Where(x => x != null))
                {
                    string extracted = Json.Str(a, "extracted_content");
                    if (!string.IsNullOrWhiteSpace(extracted))
                        sb.Append("\n\n[Attached: ").Append(Json.Str(a, "file_name") ?? "file").Append("]\n").Append(extracted.Trim());
                }
                turn.Text = sb.ToString().Trim();
                if (turn.Text.Length > 0) turns.Add(turn);
            }
            return turns;
        }
    }
}
