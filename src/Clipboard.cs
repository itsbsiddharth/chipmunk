using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Chipmunk
{
    class ClipContent
    {
        public string[] Files;
        public byte[] ImagePng;
        public string Text;
        public string SourceUrl;

        public bool IsEmpty { get { return (Files == null || Files.Length == 0) && ImagePng == null && string.IsNullOrWhiteSpace(Text); } }
    }

    static class ClipboardService
    {
        // Formats that tell clipboard managers (Ditto, Windows clipboard history) to skip an entry.
        const string ViewerIgnore = "Clipboard Viewer Ignore";
        const string ExcludeFromMonitors = "ExcludeClipboardContentFromMonitorProcessing";
        static readonly uint ViewerIgnoreId = Native.RegisterClipboardFormat(ViewerIgnore);
        static readonly uint ExcludeId = Native.RegisterClipboardFormat(ExcludeFromMonitors);

        static DateTime suppressUntil = DateTime.MinValue;
        static int suppressDepth;

        /// <summary>True while Chipmunk itself is writing to the clipboard.</summary>
        public static bool Suppressed
        {
            get { return suppressDepth > 0 || DateTime.UtcNow < suppressUntil; }
        }

        public static void BeginSuppress() { suppressDepth++; }
        public static void EndSuppress() { suppressDepth = Math.Max(0, suppressDepth - 1); suppressUntil = DateTime.UtcNow.AddMilliseconds(700); }

        public static bool IsFromMonitorExcludedSource()
        {
            return Native.IsClipboardFormatAvailable(ViewerIgnoreId) || Native.IsClipboardFormatAvailable(ExcludeId);
        }

        static void MarkTransient(DataObject d)
        {
            d.SetData(ViewerIgnore, new MemoryStream(new byte[] { 0 }));
            d.SetData(ExcludeFromMonitors, new MemoryStream(new byte[] { 1, 0, 0, 0 }));
            d.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
            d.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));
        }

        static void Put(DataObject d)
        {
            BeginSuppress();
            try { Clipboard.SetDataObject(d, true, 12, 60); }
            finally { EndSuppress(); }
        }

        public static void SetImage(byte[] png, bool transient)
        {
            var d = new DataObject();
            d.SetData("PNG", false, new MemoryStream(png));
            using (var bmp = ImageUtil.Decode(png))
            {
                d.SetData(DataFormats.Bitmap, true, bmp);
                if (transient) MarkTransient(d);
                Put(d);
            }
        }

        public static void SetText(string text, bool transient)
        {
            var d = new DataObject();
            d.SetData(DataFormats.UnicodeText, true, text);
            if (transient) MarkTransient(d);
            Put(d);
        }

        public static void SetFiles(IEnumerable<string> paths, bool transient)
        {
            var d = new DataObject();
            var sc = new StringCollection();
            foreach (var p in paths) sc.Add(p);
            d.SetFileDropList(sc);
            d.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
            if (transient) MarkTransient(d);
            Put(d);
        }

        /// <summary>Copies what is on the clipboard so it can be put back after pasting.</summary>
        public static DataObject Snapshot()
        {
            try
            {
                var src = Clipboard.GetDataObject();
                if (src == null) return null;
                var copy = new DataObject();
                int n = 0;
                foreach (var f in src.GetFormats(false))
                {
                    try
                    {
                        var v = src.GetData(f, false);
                        if (v == null) continue;
                        copy.SetData(f, false, v);
                        n++;
                    }
                    catch { }
                }
                return n > 0 ? copy : null;
            }
            catch { return null; }
        }

        public static void Restore(DataObject saved)
        {
            if (saved == null) return;
            try
            {
                MarkTransient(saved);
                Put(saved);
            }
            catch (Exception e) { Log.Error(e); }
        }

        public static ClipContent Read()
        {
            var c = new ClipContent();
            IDataObject data = null;
            for (int i = 0; i < 6 && data == null; i++)
            {
                try { data = Clipboard.GetDataObject(); }
                catch { Thread.Sleep(40); }
            }
            if (data == null) return c;
            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop))
                    c.Files = data.GetData(DataFormats.FileDrop) as string[];
            }
            catch { }
            try
            {
                if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream ms && ms.Length > 0)
                    c.ImagePng = ms.ToArray();
            }
            catch { }
            if (c.ImagePng == null)
            {
                try
                {
                    if (Clipboard.ContainsImage())
                        using (var img = Clipboard.GetImage())
                            if (img != null)
                                using (var bmp = new Bitmap(img)) c.ImagePng = ImageUtil.EncodePng(bmp);
                }
                catch { }
            }
            try
            {
                if (data.GetDataPresent(DataFormats.UnicodeText)) c.Text = data.GetData(DataFormats.UnicodeText) as string;
                else if (data.GetDataPresent(DataFormats.Text)) c.Text = data.GetData(DataFormats.Text) as string;
            }
            catch { }
            try
            {
                if (data.GetDataPresent(DataFormats.Html))
                {
                    string html = data.GetData(DataFormats.Html) as string;
                    var m = html == null ? null : Regex.Match(html, @"^SourceURL:(\S+)", RegexOptions.Multiline);
                    if (m != null && m.Success) c.SourceUrl = m.Groups[1].Value.Trim();
                }
            }
            catch { }
            return c;
        }
    }

    /// <summary>Builds the text that gets pasted: the prompt, then quotes, links and (for Claude Code) file paths.</summary>
    static class Composer
    {
        public static string Build(string prompt, IEnumerable<Attachment> atts, Func<Attachment, string> pathOf, bool withPaths)
        {
            var list = atts.Where(a => a.include).ToList();
            var sb = new StringBuilder((prompt ?? "").TrimEnd());
            var quotes = list.Where(a => a.kind == "text").ToList();
            var links = list.Where(a => a.kind == "link").ToList();
            var files = withPaths ? list.Where(a => a.kind == "image" || a.kind == "file").ToList() : new List<Attachment>();

            if (quotes.Count > 0)
            {
                sb.Append("\n\n## Quotes\n");
                foreach (var q in quotes)
                {
                    sb.Append("\nFrom ").Append(string.IsNullOrEmpty(q.source) ? "a copied snippet" : "\"" + q.source + "\"");
                    if (!string.IsNullOrEmpty(q.url)) sb.Append(" (").Append(q.url).Append(')');
                    sb.Append(":\n\"\"\"\n").Append(q.text.Trim()).Append("\n\"\"\"\n");
                }
                sb.Length -= 1; // no blank line left dangling after the last quote
                {
                }
            }
            if (links.Count > 0)
            {
                sb.Append("\n\n## Links\n");
                foreach (var l in links)
                {
                    bool named = !string.IsNullOrEmpty(l.name) && !l.url.Contains(l.name.TrimEnd('…'));
                    sb.Append("- ").Append(named ? l.name + ": " : "").Append(l.url).Append('\n');
                }
            }
            if (files.Count > 0)
            {
                sb.Append("\n\n## Attached files\n");
                foreach (var f in files) sb.Append("@\"").Append(pathOf(f)).Append("\"\n");
            }
            return sb.ToString().Trim();
        }
    }

    /// <summary>Pastes a draft into another app: each image, then files, then the text.</summary>
    static class Paster
    {
        public class Job
        {
            public IntPtr Target;
            public List<byte[]> Images = new List<byte[]>();
            public List<string> Files = new List<string>();
            public string Text;
            public bool Terminal;
            public int DelayMs = 450;
            public bool RestoreClipboard = true;
        }

        static bool StillOnTarget(IntPtr target)
        {
            return Native.Root(Native.GetForegroundWindow()) == Native.Root(target);
        }

        /// <summary>Returns null on success, or a message saying why it stopped.</summary>
        public static string Run(Job job)
        {
            DataObject saved = job.RestoreClipboard ? ClipboardService.Snapshot() : null;
            ClipboardService.BeginSuppress();
            try
            {
                if (!StillOnTarget(job.Target) && !Native.Focus(job.Target))
                    return "Couldn't switch to that window. Click into it and press the paste shortcut instead.";
                Thread.Sleep(90);
                Kbd.WaitModifiersUp(1500);

                foreach (var png in job.Images)
                {
                    if (!StillOnTarget(job.Target)) return "Stopped: you switched windows while pasting.";
                    ClipboardService.SetImage(png, true);
                    // Claude Code in a terminal reads images with Alt+V on Windows.
                    if (job.Terminal) Kbd.Chord(Kbd.ALT, Kbd.V);
                    else Kbd.Chord(Kbd.CONTROL, Kbd.V);
                    Thread.Sleep(job.DelayMs);
                }
                if (job.Files.Count > 0)
                {
                    if (!StillOnTarget(job.Target)) return "Stopped: you switched windows while pasting.";
                    ClipboardService.SetFiles(job.Files, true);
                    Kbd.Chord(Kbd.CONTROL, Kbd.V);
                    Thread.Sleep(job.DelayMs + 150);
                }
                if (!string.IsNullOrEmpty(job.Text))
                {
                    if (!StillOnTarget(job.Target)) return "Stopped: you switched windows while pasting.";
                    ClipboardService.SetText(job.Text, true);
                    // Shift+Insert pastes in every Windows terminal; Ctrl+V everywhere else.
                    if (job.Terminal) Kbd.Chord(Kbd.SHIFT, Kbd.INSERT);
                    else Kbd.Chord(Kbd.CONTROL, Kbd.V);
                    Thread.Sleep(Math.Max(250, job.DelayMs));
                }
                return null;
            }
            finally
            {
                if (saved != null) ClipboardService.Restore(saved);
                ClipboardService.EndSuppress();
            }
        }
    }
}
