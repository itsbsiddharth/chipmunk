using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Chipmunk
{
    /// <summary>Colors, fonts and small drawing helpers shared by Chipmunk's native surfaces
    /// (Chip's menu, the tray menu, toasts), so they match the panel's warm look.</summary>
    static class UiKit
    {
        public class Palette
        {
            public Color Bg, Ink, Ink2, Ink3, Hover, Line, Accent, AccentSoft, Danger, KeyBg, KeyLine, Border;
        }

        public static readonly Palette Light = new Palette
        {
            Bg = Color.FromArgb(255, 255, 255), Ink = Color.FromArgb(29, 27, 24), Ink2 = Color.FromArgb(93, 89, 81), Ink3 = Color.FromArgb(149, 144, 136),
            Hover = Color.FromArgb(243, 241, 236), Line = Color.FromArgb(236, 233, 226), Accent = Color.FromArgb(181, 96, 42), AccentSoft = Color.FromArgb(247, 234, 222),
            Danger = Color.FromArgb(179, 55, 47), KeyBg = Color.FromArgb(247, 246, 242), KeyLine = Color.FromArgb(226, 222, 213), Border = Color.FromArgb(222, 218, 209),
        };

        public static readonly Palette Dark = new Palette
        {
            Bg = Color.FromArgb(32, 31, 29), Ink = Color.FromArgb(237, 234, 228), Ink2 = Color.FromArgb(171, 166, 157), Ink3 = Color.FromArgb(123, 119, 111),
            Hover = Color.FromArgb(45, 43, 40), Line = Color.FromArgb(50, 48, 44), Accent = Color.FromArgb(224, 145, 90), AccentSoft = Color.FromArgb(58, 42, 30),
            Danger = Color.FromArgb(239, 127, 116), KeyBg = Color.FromArgb(40, 38, 35), KeyLine = Color.FromArgb(62, 59, 54), Border = Color.FromArgb(58, 55, 51),
        };

        static string textFamily, semiFamily;

        /// <summary>Segoe UI Variable on Windows 11, Segoe UI elsewhere.</summary>
        public static Font Font(float px, bool semibold = false)
        {
            if (textFamily == null)
            {
                var names = new HashSet<string>(new InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                textFamily = names.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
                semiFamily = names.Contains("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold" : "Segoe UI Semibold";
            }
            return new Font(semibold ? semiFamily : textFamily, px, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);

        /// <summary>Windows 11 rounded corners, a hairline border and the system shadow.</summary>
        public static void Elevate(IntPtr h, Palette pal, bool small = false)
        {
            try
            {
                int corner = small ? 3 : 2; // DWMWCP_ROUNDSMALL / DWMWCP_ROUND
                DwmSetWindowAttribute(h, 33, ref corner, 4);
                int border = pal.Border.R | (pal.Border.G << 8) | (pal.Border.B << 16);
                DwmSetWindowAttribute(h, 34, ref border, 4);
                var m = new MARGINS { L = 0, R = 0, T = 0, B = 1 };
                DwmExtendFrameIntoClientArea(h, ref m);
            }
            catch { }
        }

        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point p, uint flags);

        public static float ScaleAt(Point p)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out x, out y) == 0 && x > 0) return x / 96f;
            }
            catch { }
            return 1f;
        }

        // ---------------- icons: Feather-style strokes on a 24-unit grid ----------------

        public static void Glyph(Graphics g, string name, RectangleF box, Color color, float weight = 1.7f)
        {
            float s = box.Width / 24f;
            var state = g.Save();
            g.TranslateTransform(box.X, box.Y);
            g.ScaleTransform(s, s);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, weight / s * (box.Width / 16f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (name)
                {
                    case "window":
                        using (var p = Round(new RectangleF(3, 4, 18, 16), 2.5f)) g.DrawPath(pen, p);
                        g.DrawLine(pen, 3, 9, 21, 9);
                        break;
                    case "scissors":
                        g.DrawEllipse(pen, 3, 3, 6, 6);
                        g.DrawEllipse(pen, 3, 15, 6, 6);
                        g.DrawLine(pen, 20, 4, 8.12f, 15.88f);
                        g.DrawLine(pen, 14.47f, 14.48f, 20, 20);
                        g.DrawLine(pen, 8.12f, 8.12f, 12, 12);
                        break;
                    case "clock":
                        g.DrawEllipse(pen, 3, 3, 18, 18);
                        g.DrawLines(pen, new[] { new PointF(12, 7), new PointF(12, 12), new PointF(15.5f, 14) });
                        break;
                    case "eyeOff":
                        g.DrawBezier(pen, 2, 12, 5, 6, 9, 5, 12, 5);
                        g.DrawBezier(pen, 12, 5, 15, 5, 19, 6, 22, 12);
                        g.DrawBezier(pen, 22, 12, 19, 18, 15, 19, 12, 19);
                        g.DrawBezier(pen, 12, 19, 9, 19, 5, 18, 2, 12);
                        g.DrawEllipse(pen, 9, 9, 6, 6);
                        g.DrawLine(pen, 3, 3, 21, 21);
                        break;
                    case "eye":
                        g.DrawBezier(pen, 2, 12, 5, 6, 9, 5, 12, 5);
                        g.DrawBezier(pen, 12, 5, 15, 5, 19, 6, 22, 12);
                        g.DrawBezier(pen, 22, 12, 19, 18, 15, 19, 12, 19);
                        g.DrawBezier(pen, 12, 19, 9, 19, 5, 18, 2, 12);
                        g.DrawEllipse(pen, 9, 9, 6, 6);
                        break;
                    case "sliders":
                        g.DrawLine(pen, 4, 21, 4, 14); g.DrawLine(pen, 4, 10, 4, 3);
                        g.DrawLine(pen, 12, 21, 12, 12); g.DrawLine(pen, 12, 8, 12, 3);
                        g.DrawLine(pen, 20, 21, 20, 16); g.DrawLine(pen, 20, 12, 20, 3);
                        g.DrawLine(pen, 1, 14, 7, 14); g.DrawLine(pen, 9, 8, 15, 8); g.DrawLine(pen, 17, 16, 23, 16);
                        break;
                    case "power":
                        g.DrawArc(pen, 4, 5, 16, 16, -50, 280);
                        g.DrawLine(pen, 12, 2.5f, 12, 11);
                        break;
                    case "download":
                        g.DrawLines(pen, new[] { new PointF(21, 15), new PointF(21, 19), new PointF(19, 21), new PointF(5, 21), new PointF(3, 19), new PointF(3, 15) });
                        g.DrawLines(pen, new[] { new PointF(7, 10), new PointF(12, 15), new PointF(17, 10) });
                        g.DrawLine(pen, 12, 15, 12, 3);
                        break;
                    case "chevronRight":
                        g.DrawLines(pen, new[] { new PointF(9, 6), new PointF(15, 12), new PointF(9, 18) });
                        break;
                    case "chevronDown":
                        g.DrawLines(pen, new[] { new PointF(6, 9), new PointF(12, 15), new PointF(18, 9) });
                        break;
                    case "terminal":
                        g.DrawLines(pen, new[] { new PointF(4, 17), new PointF(10, 11), new PointF(4, 5) });
                        g.DrawLine(pen, 12, 19, 20, 19);
                        break;
                    case "plus":
                        g.DrawLine(pen, 12, 5, 12, 19); g.DrawLine(pen, 5, 12, 19, 12);
                        break;
                    case "inbox":
                        g.DrawLines(pen, new[] { new PointF(22, 12), new PointF(16, 12), new PointF(14, 15), new PointF(10, 15), new PointF(8, 12), new PointF(2, 12) });
                        g.DrawLines(pen, new[] { new PointF(5.45f, 5.11f), new PointF(2, 12), new PointF(2, 18), new PointF(4, 20), new PointF(20, 20), new PointF(22, 18), new PointF(22, 12), new PointF(18.55f, 5.11f), new PointF(16.76f, 4), new PointF(7.24f, 4), new PointF(5.45f, 5.11f) });
                        break;
                    default: // send
                        g.DrawLine(pen, 22, 2, 11, 13);
                        g.DrawPolygon(pen, new[] { new PointF(22, 2), new PointF(15, 22), new PointF(11, 13), new PointF(2, 9) });
                        break;
                }
            }
            g.Restore(state);
        }

        // ---------------- app icons ----------------

        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr(IntPtr h, int index);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        static readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>();

        /// <summary>The app's own icon for a window: the exe icon first (sharpest), then the window icon.</summary>
        public static Bitmap AppIcon(IntPtr h)
        {
            string exe = ExePath(h);
            string key = exe ?? ("hwnd:" + h.ToInt64());
            Bitmap cached;
            if (iconCache.TryGetValue(key, out cached)) return cached;
            Bitmap bmp = null;
            try
            {
                if (exe != null && File.Exists(exe) && exe.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) < 0)
                    using (var ic = Icon.ExtractAssociatedIcon(exe)) if (ic != null) bmp = ic.ToBitmap();
            }
            catch { }
            if (bmp == null)
            {
                try
                {
                    IntPtr hi;
                    SendMessageTimeout(h, 0x7F /* WM_GETICON */, new IntPtr(1), IntPtr.Zero, 0x2, 120, out hi);
                    if (hi == IntPtr.Zero) SendMessageTimeout(h, 0x7F, new IntPtr(2), IntPtr.Zero, 0x2, 120, out hi);
                    if (hi == IntPtr.Zero) hi = GetClassLongPtr(h, -14);
                    if (hi != IntPtr.Zero) using (var ic = Icon.FromHandle(hi)) bmp = new Bitmap(ic.ToBitmap());
                }
                catch { }
            }
            if (iconCache.Count > 64) iconCache.Clear();
            iconCache[key] = bmp;
            return bmp;
        }

        static string ExePath(IntPtr h)
        {
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                IntPtr hp = OpenProcess(0x1000, false, pid);
                if (hp == IntPtr.Zero) return null;
                try
                {
                    var sb = new StringBuilder(1024);
                    int len = sb.Capacity;
                    return QueryFullProcessImageName(hp, 0, sb, ref len) ? sb.ToString() : null;
                }
                finally { CloseHandle(hp); }
            }
            catch { return null; }
        }

        static readonly Dictionary<string, Bitmap> thumbCache = new Dictionary<string, Bitmap>();

        /// <summary>A small cover-cropped thumbnail of an image file, without locking the file.</summary>
        public static Bitmap Thumb(string path, int w, int h)
        {
            string key = path + "|" + w + "x" + h;
            Bitmap cached;
            if (thumbCache.TryGetValue(key, out cached)) return cached;
            Bitmap result = null;
            try
            {
                using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                using (var src = Image.FromStream(ms))
                {
                    result = new Bitmap(w, h);
                    using (var g = Graphics.FromImage(result))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        float s = Math.Max((float)w / src.Width, (float)h / src.Height);
                        float dw = src.Width * s, dh = src.Height * s;
                        g.DrawImage(src, (w - dw) / 2, 0, dw, dh); // top of the screenshot, where the interesting part usually is
                    }
                }
            }
            catch { }
            if (thumbCache.Count > 48) thumbCache.Clear();
            thumbCache[key] = result;
            return result;
        }
    }

    /// <summary>One row in a popover.</summary>
    class PopItem
    {
        public enum Kinds { Action, Label, Separator, Card, Toggle, Note }
        public Kinds Kind = Kinds.Action;
        public string Label, Sub, Hint, Glyph;
        public Image Icon;
        public List<Image> Thumbs;
        public Action Run;
        public bool Enabled = true, Strong, Muted, On;
        public List<PopItem> Children;   // an expandable row ("Other windows")
        public bool Expanded;
        internal RectangleF Bounds;
        internal int Depth;

        public bool Selectable { get { return Enabled && (Kind == Kinds.Action || Kind == Kinds.Card || Kind == Kinds.Toggle); } }
        public static PopItem Sep() { return new PopItem { Kind = Kinds.Separator }; }
        public static PopItem Head(string text) { return new PopItem { Kind = Kinds.Label, Label = text }; }
    }

    /// <summary>
    /// A small, keyboard-friendly menu drawn the way the panel looks: rounded, soft shadow,
    /// app icons, section labels and shortcut hints. Used for Chip and the tray icon.
    /// </summary>
    class Popover : Form
    {
        readonly List<PopItem> items;
        readonly UiKit.Palette pal;
        readonly float scale;
        readonly IntPtr returnTo;
        readonly Timer fade = new Timer { Interval = 30 };
        DateTime shownAt;
        int hover = -1;
        bool closing, acted, growUp;
        List<PopItem> flat = new List<PopItem>();
        Font fLabel, fStrong, fSub, fHead, fHint, fCardTitle;

        const int W = 304;

        public Popover(List<PopItem> items, bool dark, Point anchor)
        {
            this.items = items;
            pal = dark ? UiKit.Dark : UiKit.Light;
            scale = UiKit.ScaleAt(anchor);
            returnTo = Native.GetForegroundWindow();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = pal.Bg;
            Text = "Chipmunk menu";
            Opacity = 0;
            fLabel = UiKit.Font(13.5f * scale);
            fStrong = UiKit.Font(13.5f * scale, true);
            fSub = UiKit.Font(11.5f * scale);
            fHead = UiKit.Font(11f * scale, true);
            fHint = UiKit.Font(10.5f * scale);
            fCardTitle = UiKit.Font(14f * scale, true);
            fade.Tick += (s, e) => Watch();
            FormClosed += (s, e) => fade.Stop();
            Relayout();
        }

        bool wasForeground, mouseWasDown;

        /// <summary>Fades in, then closes the menu when you click anywhere else or switch apps.
        /// Watching the mouse (rather than only window activation) keeps this reliable even when
        /// Windows declines to give the menu keyboard focus.</summary>
        void Watch()
        {
            if (IsDisposed || closing) { fade.Stop(); return; }
            double t = (DateTime.UtcNow - shownAt).TotalMilliseconds / 110.0;
            if (Opacity < 1) Opacity = Math.Min(1, t);
            var fg = Native.GetForegroundWindow();
            if (fg == Handle) wasForeground = true;
            else if (wasForeground && fg != IntPtr.Zero) { CloseQuietly(); return; }
            bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;
            if (down && !mouseWasDown && !Bounds.Contains(Cursor.Position) && (DateTime.UtcNow - shownAt).TotalMilliseconds > 150) { CloseQuietly(); return; }
            mouseWasDown = down;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 /* TOOLWINDOW */ | 0x00000008 /* TOPMOST */;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW, for Windows 10
                return cp;
            }
        }

        float S(float v) { return v * scale; }

        /// <summary>Opens the menu next to a point or rectangle, kept inside the screen.</summary>
        public void ShowAt(Rectangle anchor, bool preferAbove, bool alignRight)
        {
            var wa = Screen.FromPoint(new Point(anchor.X + anchor.Width / 2, anchor.Y + anchor.Height / 2)).WorkingArea;
            int x = alignRight ? anchor.Right - Width : anchor.Left;
            int y = preferAbove ? anchor.Top - Height - (int)S(8) : anchor.Bottom + (int)S(8);
            if (y < wa.Top + 4) y = anchor.Bottom + (int)S(8);
            if (y + Height > wa.Bottom - 4) y = Math.Max(wa.Top + 4, wa.Bottom - Height - 4);
            x = Math.Max(wa.Left + 4, Math.Min(x, wa.Right - Width - 4));
            growUp = y + Height <= anchor.Top;
            Location = new Point(x, y);
            Show();
            UiKit.Elevate(Handle, pal);
            Native.SetDarkTitleBar(Handle, pal == UiKit.Dark);
            Activate();
            Native.Focus(Handle);
            shownAt = DateTime.UtcNow;
            fade.Start();
            hover = flat.FindIndex(i => i.Selectable && i.Kind != PopItem.Kinds.Card);
            Invalidate();
        }

        // ---------------- layout ----------------

        void Relayout()
        {
            flat = new List<PopItem>();
            foreach (var it in items)
            {
                it.Depth = 0;
                flat.Add(it);
                if (it.Children != null && it.Expanded)
                    foreach (var c in it.Children) { c.Depth = 1; flat.Add(c); }
            }
            float y = S(6);
            foreach (var it in flat)
            {
                float h;
                switch (it.Kind)
                {
                    case PopItem.Kinds.Separator: h = S(9); break;
                    case PopItem.Kinds.Label: h = S(26); break;
                    case PopItem.Kinds.Card: h = S(64); break;
                    case PopItem.Kinds.Note: h = S(40); break;
                    default: h = it.Sub != null ? S(44) : S(36); break;
                }
                it.Bounds = new RectangleF(S(6), y, S(W) - S(12), h);
                y += h;
            }
            ClientSize = new Size((int)S(W), (int)(y + S(6)));
        }

        // ---------------- drawing ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(pal.Bg);
            for (int i = 0; i < flat.Count; i++) DrawItem(g, flat[i], i == hover);
        }

        void DrawItem(Graphics g, PopItem it, bool hot)
        {
            var r = it.Bounds;
            switch (it.Kind)
            {
                case PopItem.Kinds.Separator:
                    using (var p = new Pen(pal.Line, Math.Max(1, S(1)))) g.DrawLine(p, r.X + S(6), r.Y + r.Height / 2, r.Right - S(6), r.Y + r.Height / 2);
                    return;
                case PopItem.Kinds.Label:
                    TextRenderer.DrawText(g, it.Label.ToUpperInvariant(), fHead, Rect(r.X + S(10), r.Y + S(8), r.Width - S(20), r.Height - S(8)), pal.Ink3, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
                    return;
                case PopItem.Kinds.Note:
                    TextRenderer.DrawText(g, it.Label, fSub, Rect(r.X + S(10), r.Y, r.Width - S(20), r.Height), pal.Ink3, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    return;
            }

            if (hot)
                using (var b = new SolidBrush(it.Kind == PopItem.Kinds.Card ? pal.Hover : pal.Hover))
                using (var path = UiKit.Round(r, S(8)))
                    g.FillPath(b, path);

            float indent = it.Depth * S(14);
            if (it.Kind == PopItem.Kinds.Card) { DrawCard(g, it, r); return; }

            // icon
            var iconBox = new RectangleF(r.X + S(10) + indent, r.Y + (r.Height - S(20)) / 2, S(20), S(20));
            if (it.Icon != null)
            {
                var st = g.Save();
                if (!it.Enabled) { /* drawn faded below */ }
                g.DrawImage(it.Icon, iconBox);
                g.Restore(st);
                if (!it.Enabled) using (var veil = new SolidBrush(Color.FromArgb(150, pal.Bg))) g.FillRectangle(veil, iconBox);
            }
            else if (it.Glyph != null)
            {
                var gb = new RectangleF(iconBox.X + S(1.5f), iconBox.Y + S(1.5f), S(17), S(17));
                UiKit.Glyph(g, it.Glyph, gb, it.Enabled ? (it.Muted ? pal.Ink3 : pal.Ink2) : pal.Line, 1.7f);
            }

            // text
            float tx = iconBox.Right + S(11);
            float right = r.Right - S(10);
            float hintW = 0;
            string hint = it.Hint;
            if (hot && it.Enabled && hint == null && it.Kind == PopItem.Kinds.Action && it.Strong) hint = "↵";
            if (it.Kind == PopItem.Kinds.Toggle) hintW = S(30) + S(6);
            else if (it.Children != null) hintW = S(18);
            else if (hint != null) hintW = DrawHint(g, hint, right, r) + S(8);

            Color ink = !it.Enabled ? pal.Ink3 : it.Muted ? pal.Ink2 : pal.Ink;
            var font = it.Strong ? fStrong : fLabel;
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (it.Sub != null)
            {
                TextRenderer.DrawText(g, it.Label, font, Rect(tx, r.Y + S(5), right - tx - hintW, S(20)), ink, flags | TextFormatFlags.Bottom);
                TextRenderer.DrawText(g, it.Sub, fSub, Rect(tx, r.Y + S(24), right - tx - hintW, S(16)), pal.Ink3, flags | TextFormatFlags.Top);
            }
            else TextRenderer.DrawText(g, it.Label, font, Rect(tx, r.Y, right - tx - hintW, r.Height), ink, flags | TextFormatFlags.VerticalCenter);

            if (it.Children != null)
                UiKit.Glyph(g, it.Expanded ? "chevronDown" : "chevronRight", new RectangleF(right - S(15), r.Y + (r.Height - S(15)) / 2, S(15), S(15)), pal.Ink3, 1.8f);
            if (it.Kind == PopItem.Kinds.Toggle) DrawSwitch(g, it.On, new RectangleF(right - S(30), r.Y + (r.Height - S(18)) / 2, S(30), S(18)));
        }

        void DrawCard(Graphics g, PopItem it, RectangleF r)
        {
            float x = r.X + S(10);
            float tw = S(44), th = S(34);
            var thumbs = it.Thumbs ?? new List<Image>();
            float stackW = 0;
            if (thumbs.Count > 0)
            {
                for (int i = Math.Min(3, thumbs.Count) - 1; i >= 0; i--)
                {
                    float shrink = i * S(4);
                    var tr = new RectangleF(x + i * S(9) + shrink, r.Y + (r.Height - th) / 2 + shrink / 2 - i * S(1), tw - shrink, th - shrink);
                    using (var path = UiKit.Round(tr, S(6)))
                    {
                        var st = g.Save();
                        g.SetClip(path);
                        g.DrawImage(thumbs[i], tr);
                        g.Restore(st);
                        using (var p = new Pen(pal.Bg, S(2))) g.DrawPath(p, path);
                        using (var p = new Pen(Color.FromArgb(40, 0, 0, 0), S(1))) g.DrawPath(p, path);
                    }
                }
                stackW = tw + (Math.Min(3, thumbs.Count) - 1) * S(9);
            }
            else
            {
                var tr = new RectangleF(x, r.Y + (r.Height - th) / 2, tw, th);
                using (var path = UiKit.Round(tr, S(7)))
                using (var b = new SolidBrush(pal.AccentSoft)) g.FillPath(b, path);
                UiKit.Glyph(g, "plus", new RectangleF(tr.X + (tw - S(16)) / 2, tr.Y + (th - S(16)) / 2, S(16), S(16)), pal.Accent, 1.9f);
                stackW = tw;
            }
            float tx = x + stackW + S(12);
            float right = r.Right - S(10);
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, it.Label, fCardTitle, Rect(tx, r.Y + S(12), right - tx - S(18), S(21)), pal.Ink, flags | TextFormatFlags.Bottom);
            TextRenderer.DrawText(g, it.Sub ?? "", fSub, Rect(tx, r.Y + S(34), right - tx - S(18), S(17)), pal.Ink3, flags | TextFormatFlags.Top);
            UiKit.Glyph(g, "chevronRight", new RectangleF(right - S(15), r.Y + (r.Height - S(15)) / 2, S(15), S(15)), pal.Ink3, 1.8f);
        }

        float DrawHint(Graphics g, string hint, float right, RectangleF row)
        {
            var size = TextRenderer.MeasureText(g, hint, fHint, Size.Empty, TextFormatFlags.NoPadding);
            float w = size.Width + S(12), h = S(20);
            var kr = new RectangleF(right - w, row.Y + (row.Height - h) / 2, w, h);
            using (var path = UiKit.Round(kr, S(5)))
            using (var b = new SolidBrush(pal.KeyBg))
            using (var p = new Pen(pal.KeyLine, Math.Max(1, S(1))))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, hint, fHint, Rect(kr.X, kr.Y, kr.Width, kr.Height), pal.Ink3, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            return w;
        }

        void DrawSwitch(Graphics g, bool on, RectangleF r)
        {
            using (var path = UiKit.Round(r, r.Height / 2))
            using (var b = new SolidBrush(on ? pal.Accent : pal.KeyLine)) g.FillPath(b, path);
            float d = r.Height - S(4);
            float kx = on ? r.Right - d - S(2) : r.X + S(2);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, r.Y + S(2), d, d);
        }

        static Rectangle Rect(float x, float y, float w, float h) { return new Rectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Max(1, Math.Round(w)), (int)Math.Max(1, Math.Round(h))); }

        // ---------------- input ----------------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = flat.FindIndex(it => it.Selectable && it.Bounds.Contains(e.Location));
            if (i != hover) { hover = i; Invalidate(); }
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = flat.FindIndex(it => it.Selectable && it.Bounds.Contains(e.Location));
            if (i >= 0) Invoke(flat[i]);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            switch (key)
            {
                case Keys.Escape: CloseQuietly(); return true;
                case Keys.Down: Step(1); return true;
                case Keys.Up: Step(-1); return true;
                case Keys.Tab: Step(1); return true;
                case Keys.Shift | Keys.Tab: Step(-1); return true;
                case Keys.Enter:
                case Keys.Space:
                    if (hover >= 0 && hover < flat.Count) Invoke(flat[hover]);
                    return true;
                case Keys.Right:
                    if (hover >= 0 && flat[hover].Children != null && !flat[hover].Expanded) Invoke(flat[hover]);
                    return true;
                case Keys.Left:
                    if (hover >= 0)
                    {
                        var parent = flat[hover].Children != null ? flat[hover] : (flat[hover].Depth == 1 ? items.FirstOrDefault(p => p.Children != null && p.Children.Contains(flat[hover])) : null);
                        if (parent != null && parent.Expanded) { parent.Expanded = false; Relayout(); hover = flat.IndexOf(parent); Invalidate(); }
                    }
                    return true;
            }
            return base.ProcessCmdKey(ref msg, key);
        }

        void Step(int dir)
        {
            if (flat.Count == 0) return;
            int i = hover;
            for (int n = 0; n < flat.Count; n++)
            {
                i = (i + dir + flat.Count) % flat.Count;
                if (flat[i].Selectable) { hover = i; Invalidate(); return; }
            }
        }

        void Invoke(PopItem it)
        {
            if (!it.Enabled) return;
            if (it.Children != null)
            {
                it.Expanded = !it.Expanded;
                int bottom = Bottom;
                Relayout();
                hover = flat.IndexOf(it);
                // a menu that opened above its anchor grows upward; any menu stays on screen
                var wa = Screen.FromControl(this).WorkingArea;
                if (growUp) Top = bottom - Height;
                if (Bottom > wa.Bottom - 4) Top = wa.Bottom - 4 - Height;
                if (Top < wa.Top + 4) Top = wa.Top + 4;
                Invalidate();
                return;
            }
            if (it.Kind == PopItem.Kinds.Toggle)
            {
                it.On = !it.On;
                Invalidate();
                it.Run?.Invoke();
                return;
            }
            acted = true;
            closing = true;
            var run = it.Run;
            Hide();
            BeginInvoke((Action)(() =>
            {
                try { run?.Invoke(); }
                catch (Exception e) { Log.Error(e); }
                Close();
            }));
        }

        void CloseQuietly()
        {
            if (closing || acted) return;
            closing = true;
            // give focus back to whatever you were using before the menu opened
            if (returnTo != IntPtr.Zero && Native.IsWindow(returnTo) && Native.GetForegroundWindow() == Handle) Native.Focus(returnTo);
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fade.Dispose();
                foreach (var f in new[] { fLabel, fStrong, fSub, fHead, fHint, fCardTitle }) f?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
