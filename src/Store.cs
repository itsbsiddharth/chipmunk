using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Chipmunk
{
    public class CheckItem
    {
        public string id;
        public string text;
        public bool done;
        public string attId;
    }

    public class Attachment
    {
        public string id;
        public string kind;      // image | file | text | link
        public string name;
        public string file;      // path relative to the draft folder (image, file)
        public string linkPath;  // big files are linked where they are instead of copied
        public string text;      // quote text
        public string url;       // link target, or the page a quote came from
        public string source;    // where it came from, e.g. "Snip · Chrome"
        public string ext;
        public string added;
        public string hash;
        public bool include = true;
        public int w;
        public int h;
        public long bytes;
        public int removedIndex = -1;
    }

    public class SendRecord
    {
        public string at;
        public string target;
        public int items;
    }

    public class Draft
    {
        public string id;
        public string title = "";
        public string status = "draft";
        public bool pinned;
        public bool imported;
        public string created;
        public string updated;
        public string armedItem;
        public List<CheckItem> checklist = new List<CheckItem>();
        public List<Attachment> attachments = new List<Attachment>();
        public List<Attachment> removed = new List<Attachment>();
        public List<SendRecord> sends = new List<SendRecord>();
    }

    /// <summary>
    /// Drafts live as plain folders: drafts/&lt;id&gt;/prompt.md, draft.json, files/, .versions/.
    /// Every change is written straight to disk.
    /// </summary>
    class Store
    {
        public readonly string Root;
        public readonly string DraftsDir;
        public readonly string TrashDir;
        readonly Dictionary<string, Draft> drafts = new Dictionary<string, Draft>();
        readonly Dictionary<string, DateTime> lastVersionAt = new Dictionary<string, DateTime>();
        static readonly TimeSpan VersionEvery = TimeSpan.FromSeconds(60);
        const int MaxVersions = 150;

        public Store(string root)
        {
            Root = root;
            DraftsDir = Path.Combine(root, "drafts");
            TrashDir = Path.Combine(root, ".trash");
            Directory.CreateDirectory(DraftsDir);
            WriteReadme();
            foreach (var dir in Directory.GetDirectories(DraftsDir))
            {
                var d = LoadDraft(dir);
                if (d != null) drafts[d.id] = d;
            }
        }

        void WriteReadme()
        {
            string p = Path.Combine(Root, "README.txt");
            if (File.Exists(p) && !File.ReadAllText(p).Contains("Promptpad")) return;
            try
            {
                File.WriteAllText(p,
                    "These are your Chipmunk drafts.\r\n\r\n" +
                    "Each folder in drafts\\ is one draft:\r\n" +
                    "  prompt.md    the prompt text (always the latest)\r\n" +
                    "  files\\       screenshots and files you added\r\n" +
                    "  .versions\\   earlier versions of the text, one per minute of editing\r\n" +
                    "  draft.json   title, reminders and attachment details\r\n\r\n" +
                    "Deleted drafts go to .trash\\ and can be restored by moving them back.\r\n");
            }
            catch { }
        }

        static string Now() { return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); }

        static Draft LoadDraft(string dir)
        {
            string meta = Path.Combine(dir, "draft.json");
            try
            {
                Draft d = File.Exists(meta) ? Json.Deserialize<Draft>(File.ReadAllText(meta)) : null;
                if (d == null)
                {
                    d = new Draft { id = Path.GetFileName(dir), created = Now(), updated = Now() };
                }
                d.id = Path.GetFileName(dir);
                d.checklist = d.checklist ?? new List<CheckItem>();
                d.attachments = d.attachments ?? new List<Attachment>();
                d.removed = d.removed ?? new List<Attachment>();
                d.sends = d.sends ?? new List<SendRecord>();
                d.updated = d.updated ?? d.created ?? Now();
                return d;
            }
            catch (Exception e)
            {
                Log.Error("Could not read " + meta + ": " + e.Message);
                return null;
            }
        }

        public IEnumerable<Draft> All { get { return drafts.Values; } }
        public int Count { get { return drafts.Count; } }

        public bool Exists(string id) { return id != null && drafts.ContainsKey(id); }

        public Draft Get(string id)
        {
            Draft d;
            if (id != null && drafts.TryGetValue(id, out d)) return d;
            throw new InvalidOperationException("That draft no longer exists.");
        }

        public string Dir(Draft d) { return Path.Combine(DraftsDir, d.id); }
        string TextPath(Draft d) { return Path.Combine(Dir(d), "prompt.md"); }
        string VersionsDir(Draft d) { return Path.Combine(Dir(d), ".versions"); }
        public string FilePath(Draft d, Attachment a)
        {
            if (a.linkPath != null) return a.linkPath;
            return a.file == null ? null : Path.Combine(Dir(d), a.file.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>Files bigger than this stay where they are and are linked, so drafts don't duplicate them.</summary>
        public const long LinkThreshold = 25L * 1024 * 1024;

        static string NewId()
        {
            return "d" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        static string NewAttId() { return "a" + Guid.NewGuid().ToString("N").Substring(0, 10); }

        public Draft Create(string title)
        {
            var d = new Draft { id = NewId(), title = title ?? "", created = Now(), updated = Now() };
            Directory.CreateDirectory(Path.Combine(Dir(d), "files"));
            File.WriteAllText(TextPath(d), "");
            drafts[d.id] = d;
            SaveMeta(d);
            return d;
        }

        public Draft Duplicate(Draft src)
        {
            var d = Create(string.IsNullOrWhiteSpace(src.title) ? "Copy of draft" : src.title + " (copy)");
            File.WriteAllText(TextPath(d), ReadText(src), new UTF8Encoding(false));
            string from = Path.Combine(Dir(src), "files");
            string to = Path.Combine(Dir(d), "files");
            if (Directory.Exists(from))
                foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            d.attachments = src.attachments.Select(Clone).ToList();
            d.checklist = src.checklist.Select(c => new CheckItem { id = c.id, text = c.text, done = c.done, attId = c.attId }).ToList();
            SaveMeta(d);
            return d;
        }

        static Attachment Clone(Attachment a)
        {
            return Json.Deserialize<Attachment>(Json.Serialize(a));
        }

        public string ReadText(Draft d)
        {
            try { return File.Exists(TextPath(d)) ? File.ReadAllText(TextPath(d)) : ""; }
            catch (IOException) { return ""; }
        }

        public void SaveText(Draft d, string text)
        {
            text = text ?? "";
            string old = ReadText(d);
            if (old == text) return;
            MaybeSnapshot(d, old);
            Json.WriteAtomic(TextPath(d), text);
            Touch(d);
        }

        // Keeps the text as it was before this round of edits, at most once a minute.
        void MaybeSnapshot(Draft d, string old)
        {
            if (string.IsNullOrWhiteSpace(old)) return;
            DateTime last;
            if (lastVersionAt.TryGetValue(d.id, out last) && DateTime.UtcNow - last < VersionEvery) return;
            WriteVersion(d, old);
        }

        void WriteVersion(Draft d, string text)
        {
            try
            {
                string dir = VersionsDir(d);
                Directory.CreateDirectory(dir);
                var latest = Directory.GetFiles(dir, "*.md").OrderByDescending(f => f).FirstOrDefault();
                if (latest != null && File.ReadAllText(latest) == text) { lastVersionAt[d.id] = DateTime.UtcNow; return; }
                string name = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".md";
                File.WriteAllText(Path.Combine(dir, name), text, new UTF8Encoding(false));
                lastVersionAt[d.id] = DateTime.UtcNow;
                foreach (var extra in Directory.GetFiles(dir, "*.md").OrderByDescending(f => f).Skip(MaxVersions)) File.Delete(extra);
            }
            catch (Exception e) { Log.Error(e); }
        }

        public List<Dictionary<string, object>> Versions(Draft d)
        {
            var list = new List<Dictionary<string, object>>();
            string dir = VersionsDir(d);
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.md").OrderByDescending(x => x))
            {
                string text = File.ReadAllText(f);
                DateTime t;
                DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out t);
                list.Add(new Dictionary<string, object>
                {
                    { "name", Path.GetFileName(f) },
                    { "time", t.ToString("o", CultureInfo.InvariantCulture) },
                    { "preview", FirstLine(text, 90) },
                    { "words", Regex.Matches(text, @"\S+").Count },
                });
            }
            return list;
        }

        public string ReadVersion(Draft d, string name)
        {
            string p = Path.Combine(VersionsDir(d), Path.GetFileName(name));
            if (!File.Exists(p)) throw new InvalidOperationException("That version is gone.");
            return File.ReadAllText(p);
        }

        public void RestoreVersion(Draft d, string name)
        {
            string text = ReadVersion(d, name);
            string current = ReadText(d);
            if (!string.IsNullOrWhiteSpace(current)) WriteVersion(d, current);
            Json.WriteAtomic(TextPath(d), text);
            Touch(d);
        }

        public void Touch(Draft d)
        {
            d.updated = Now();
            SaveMeta(d);
        }

        public void SaveMeta(Draft d)
        {
            Json.WriteAtomic(Path.Combine(Dir(d), "draft.json"), Json.Serialize(d));
        }

        public void Delete(Draft d)
        {
            Directory.CreateDirectory(TrashDir);
            string dest = Path.Combine(TrashDir, d.id);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.Move(Dir(d), dest);
            drafts.Remove(d.id);
        }

        public Draft Undelete(string id)
        {
            string src = Path.Combine(TrashDir, Path.GetFileName(id));
            if (!Directory.Exists(src)) throw new InvalidOperationException("That draft is no longer in the trash.");
            string dest = Path.Combine(DraftsDir, Path.GetFileName(id));
            Directory.Move(src, dest);
            var d = LoadDraft(dest);
            drafts[d.id] = d;
            Touch(d);
            return d;
        }

        // ---------- attachments ----------

        static string Hash(byte[] data)
        {
            using (var sha = SHA1.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        string UniqueFileName(Draft d, string name)
        {
            string dir = Path.Combine(Dir(d), "files");
            Directory.CreateDirectory(dir);
            name = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
            if (name.Length == 0) name = "file";
            if (name.Length > 100) name = name.Substring(0, 80) + Path.GetExtension(name);
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            string candidate = name;
            for (int i = 2; File.Exists(Path.Combine(dir, candidate)); i++) candidate = stem + " (" + i + ")" + ext;
            return candidate;
        }

        Attachment FindByHash(Draft d, string hash)
        {
            return d.attachments.FirstOrDefault(a => a.hash == hash);
        }

        /// <summary>Adds image bytes. Returns null if the same image is already in the draft.</summary>
        public Attachment AddImage(Draft d, byte[] data, string mediaType, string name, string source, string linkItem)
        {
            string hash = Hash(data);
            if (FindByHash(d, hash) != null) return null;
            var size = ImageUtil.SizeOf(data);
            string ext = ImageUtil.ExtensionFor(mediaType, data);
            string fileName = UniqueFileName(d, "shot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ext);
            File.WriteAllBytes(Path.Combine(Dir(d), "files", fileName), data);
            var a = new Attachment
            {
                id = NewAttId(), kind = "image", name = name ?? ScreenshotName(), file = "files/" + fileName, source = source,
                ext = ext.TrimStart('.'), hash = hash, w = size.Width, h = size.Height, bytes = data.LongLength, added = Now(),
            };
            return Insert(d, a, linkItem);
        }

        public static string ScreenshotName()
        {
            return "Screenshot " + DateTime.Now.ToString("h:mm tt", CultureInfo.CurrentCulture);
        }

        public Attachment AddFile(Draft d, string path, string source, string linkItem)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) throw new InvalidOperationException("Can't find " + path);
            if (fi.Length > LinkThreshold)
            {
                string key = "link:" + fi.FullName.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
                if (FindByHash(d, key) != null) return null;
                return Insert(d, new Attachment
                {
                    id = NewAttId(), kind = "file", name = fi.Name, linkPath = fi.FullName, source = source,
                    ext = fi.Extension.TrimStart('.').ToLowerInvariant(), hash = key, bytes = fi.Length, added = Now(),
                }, linkItem);
            }
            byte[] data = File.ReadAllBytes(path);
            string hash = Hash(data);
            if (FindByHash(d, hash) != null) return null;
            string fileName = UniqueFileName(d, fi.Name);
            File.WriteAllBytes(Path.Combine(Dir(d), "files", fileName), data);
            bool isImage = ImageUtil.IsImageFile(path);
            var a = new Attachment
            {
                id = NewAttId(), kind = isImage ? "image" : "file", name = fi.Name, file = "files/" + fileName, source = source,
                ext = fi.Extension.TrimStart('.').ToLowerInvariant(), hash = hash, bytes = data.LongLength, added = Now(),
            };
            if (isImage)
            {
                var size = ImageUtil.SizeOf(data);
                a.w = size.Width;
                a.h = size.Height;
            }
            return Insert(d, a, linkItem);
        }

        public Attachment AddText(Draft d, string text, string source, string url, string linkItem)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) throw new InvalidOperationException("There was no text to add.");
            string hash = Hash(Encoding.UTF8.GetBytes(text));
            if (FindByHash(d, hash) != null) return null;
            var a = new Attachment { id = NewAttId(), kind = "text", text = text, source = source, url = url, hash = hash, name = FirstLine(text, 60), added = Now() };
            return Insert(d, a, linkItem);
        }

        public Attachment AddLink(Draft d, string url, string title, string linkItem)
        {
            url = (url ?? "").Trim();
            string hash = Hash(Encoding.UTF8.GetBytes(url));
            if (FindByHash(d, hash) != null) return null;
            string name = string.IsNullOrWhiteSpace(title) ? Regex.Replace(url, @"^https?://(www\.)?", "") : title.Trim();
            if (name.Length > 80) name = name.Substring(0, 80) + "…";
            var a = new Attachment { id = NewAttId(), kind = "link", url = url, name = name, hash = hash, added = Now() };
            return Insert(d, a, linkItem);
        }

        Attachment Insert(Draft d, Attachment a, string linkItem)
        {
            d.attachments.Add(a);
            CheckItem target = null;
            if (linkItem != null) target = d.checklist.FirstOrDefault(i => i.id == linkItem);
            if (target == null && d.armedItem != null) target = d.checklist.FirstOrDefault(i => i.id == d.armedItem && !i.done);
            if (target == null) target = d.checklist.FirstOrDefault(i => !i.done && i.attId == null && InferKind(i.text) == a.kind);
            if (target != null)
            {
                target.done = true;
                target.attId = a.id;
            }
            d.armedItem = null;
            Touch(d);
            return a;
        }

        /// <summary>Guesses what kind of attachment a reminder is waiting for.</summary>
        public static string InferKind(string text)
        {
            string t = (text ?? "").ToLowerInvariant();
            if (Regex.IsMatch(t, @"screen ?shot|image|photo|picture|snip|\bui\b|\bpic\b")) return "image";
            if (Regex.IsMatch(t, @"\.(csv|pdf|docx?|txt|json|ipynb|py|js|ts|log|zip|xlsx?|md)\b|\bfile\b|notebook|\blogs?\b")) return "file";
            if (Regex.IsMatch(t, @"\blink\b|\burl\b|\bdocs?\b|source|article|\bpage\b")) return "link";
            if (Regex.IsMatch(t, @"quote|snippet|message|paragraph|\btext\b")) return "text";
            return null;
        }

        public void RemoveAttachment(Draft d, string attId)
        {
            int i = d.attachments.FindIndex(a => a.id == attId);
            if (i < 0) return;
            var att = d.attachments[i];
            att.removedIndex = i;
            d.attachments.RemoveAt(i);
            d.removed.RemoveAll(r => r.id == attId);
            d.removed.Add(att);
            if (d.removed.Count > 50) d.removed.RemoveAt(0);
            foreach (var it in d.checklist.Where(c => c.attId == attId))
            {
                it.attId = null;
                it.done = false;
            }
            Touch(d);
        }

        public void RestoreAttachment(Draft d, string attId)
        {
            var att = d.removed.FirstOrDefault(r => r.id == attId);
            if (att == null) return;
            d.removed.Remove(att);
            int at = Math.Max(0, Math.Min(att.removedIndex, d.attachments.Count));
            att.removedIndex = -1;
            d.attachments.Insert(at, att);
            Touch(d);
        }

        public static string FirstLine(string text, int max)
        {
            foreach (var line in (text ?? "").Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                return t.Length > max ? t.Substring(0, max).TrimEnd() + "…" : t;
            }
            return "";
        }
    }
}
