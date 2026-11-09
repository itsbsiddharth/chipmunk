using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Chipmunk
{
    /// <summary>
    /// Chip, the desktop assistant. It lives at the edge of the screen and earns its place:
    /// click it to send your draft to any open AI app, drop anything on it to collect it, and it
    /// speaks up in a bubble when something happens ("Sent to ChatGPT") or when it can help
    /// ("Add this screenshot?"). Its cheeks fill up as the draft does. Drawn with per-pixel
    /// alpha so only Chip and its bubble are on screen; everything else clicks through.
    /// </summary>
    class PetWindow : Form
    {
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);

        const int PetPx = 92, WinW = 320, WinH = 210;

        readonly AppCore core;
        readonly Timer timer = new Timer { Interval = 33 };
        readonly Random rnd = new Random();
        float scale = 1f;
        int count;
        string draftTitle;
        float puff, targetPuff;
        int mode;                 // 0 idle, 2 working (sending)
        DateTime catchAt = DateTime.MinValue;
        DateTime nextBlink = DateTime.UtcNow.AddSeconds(3);
        DateTime blinkUntil = DateTime.MinValue;
        bool hovering, dragOver, suppressed, wanted, bubbleLeft = true;
        Point downAt, startLoc;
        bool mouseDown, dragging;
        readonly DateTime started = DateTime.UtcNow;
        bool loggedFailure;

        string bubble;
        string bubbleCta;
        Action bubbleAction;
        DateTime bubbleShown = DateTime.MinValue, bubbleUntil = DateTime.MinValue;
        RectangleF bubbleRect, petRect;

        public PetWindow(AppCore core)
        {
            this.core = core;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AllowDrop = true;
            Text = "Chip";
            timer.Tick += (s, e) => Render();
            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += OnUp;
            MouseLeave += (s, e) => { hovering = false; };
            DragEnter += (s, e) =>
            {
                e.Effect = PetDrop.CanAccept(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                dragOver = e.Effect != DragDropEffects.None;
                if (dragOver) Say("Drop it, I'll hold it.", 1500);
            };
            DragLeave += (s, e) => { dragOver = false; };
            DragDrop += (s, e) =>
            {
                dragOver = false;
                core.AddFromDrop(e.Data, "Dropped on Chip");
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 /* LAYERED */ | 0x00000080 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */ | 0x00000008 /* TOPMOST */;
                return cp;
            }
        }

        // ---------------- placement and visibility ----------------

        public void Place(int[] saved)
        {
            if (!IsHandleCreated) CreateHandle();
            try { scale = Math.Max(1f, GetDpiForWindow(Handle) / 96f); } catch { scale = 1f; }
            Size = new Size((int)(WinW * scale), (int)(WinH * scale));
            var wa = Screen.PrimaryScreen.WorkingArea;
            Point p = new Point(wa.Right - Width - (int)(16 * scale), wa.Bottom - Height + (int)(4 * scale));
            if (saved != null && saved.Length == 2)
            {
                var r = new Rectangle(saved[0], saved[1], Width, Height);
                foreach (var s in Screen.AllScreens)
                    if (s.WorkingArea.IntersectsWith(r)) { p = new Point(saved[0], saved[1]); break; }
            }
            Location = p;
            Arrange();
        }

        /// <summary>Chip sits on the side of the window nearest the screen edge; the bubble opens toward the middle.</summary>
        void Arrange()
        {
            var screen = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).WorkingArea;
            bubbleLeft = Left + Width / 2 > screen.Left + screen.Width / 2;
            float p = PetPx * scale;
            petRect = new RectangleF(bubbleLeft ? Width - p - 4 * scale : 4 * scale, Height - p, p, p);
        }

        public void SetWanted(bool on) { wanted = on; ApplyVisibility(); }

        public void SetSuppressed(bool on)
        {
            if (suppressed == on) return;
            suppressed = on;
            ApplyVisibility();
        }

        public bool IsShowing { get { return Visible; } }

        void ApplyVisibility()
        {
            bool show = wanted && !suppressed;
            if (show && !Visible)
            {
                lastFrame = null;
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                Visible = true;
                timer.Start();
                Render();
            }
            else if (!show && Visible)
            {
                timer.Stop();
                Hide();
            }
        }

        // ---------------- what Chip shows ----------------

        public void SetCount(int n, string title)
        {
            count = Math.Max(0, n);
            draftTitle = title;
            targetPuff = Math.Min(1f, count / 6f);
        }

        public void Catch() { catchAt = DateTime.UtcNow; }
        public void SetMode(int m) { mode = m; }

        /// <summary>Shows a speech bubble. With an action, clicking the bubble (or Chip) runs it.</summary>
        public void Say(string text, int ms = 3200, Action action = null, string cta = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            bubble = text;
            bubbleAction = action;
            bubbleCta = action != null ? (cta ?? "Click to do it") : null;
            bubbleShown = DateTime.UtcNow;
            bubbleUntil = DateTime.UtcNow.AddMilliseconds(ms);
        }

        void ClearBubble() { bubbleUntil = DateTime.MinValue; bubbleAction = null; }

        // ---------------- mouse ----------------

        void OnDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !petRect.Contains(e.Location)) return;
            mouseDown = true;
            dragging = false;
            downAt = Cursor.Position;
            startLoc = Location;
        }

        void OnMove(object s, MouseEventArgs e)
        {
            hovering = petRect.Contains(e.Location) || (BubbleVisible && bubbleRect.Contains(e.Location));
            Cursor = hovering ? Cursors.Hand : Cursors.Default;
            if (!mouseDown) return;
            var c = Cursor.Position;
            if (!dragging && (Math.Abs(c.X - downAt.X) > 4 || Math.Abs(c.Y - downAt.Y) > 4)) dragging = true;
            if (dragging) Location = new Point(startLoc.X + c.X - downAt.X, startLoc.Y + c.Y - downAt.Y);
        }

        void OnUp(object s, MouseEventArgs e)
        {
            if (BubbleVisible && bubbleRect.Contains(e.Location) && !mouseDown)
            {
                var act = bubbleAction;
                ClearBubble();
                if (act != null) act();
                return;
            }
            if (e.Button == MouseButtons.Right && petRect.Contains(e.Location)) { ShowMenu(); return; }
            if (e.Button != MouseButtons.Left || !mouseDown) return;
            mouseDown = false;
            if (dragging)
            {
                Arrange();
                core.SavePetPosition(new[] { Location.X, Location.Y });
            }
            else if (BubbleVisible && bubbleAction != null)
            {
                var act = bubbleAction;
                ClearBubble();
                act();
            }
            else ShowMenu();
            dragging = false;
        }

        bool BubbleVisible { get { return bubble != null && DateTime.UtcNow < bubbleUntil; } }

        // ---------------- the menu: send anywhere in one click ----------------

        void ShowMenu()
        {
            ClearBubble();
            var menu = new ContextMenuStrip { Renderer = new ChipMenuRenderer(), ShowImageMargin = false, Font = new Font("Segoe UI", 9.75f) };
            menu.Padding = new Padding(4);
            string title = string.IsNullOrWhiteSpace(draftTitle) ? "Untitled draft" : draftTitle;
            if (title.Length > 38) title = title.Substring(0, 36) + "…";
            menu.Items.Add(new ToolStripLabel(title + (count > 0 ? "  ·  " + count + " collected" : "")) { ForeColor = Color.FromArgb(149, 144, 136), Margin = new Padding(6, 4, 6, 4) });
            menu.Items.Add(new ToolStripMenuItem("Open draft", null, (s, e) => core.ShowPanel()) { Font = new Font("Segoe UI Semibold", 9.75f) });

            var targets = core.SendTargets();
            var ai = targets.FindAll(t => (bool)t["ai"]);
            var other = targets.FindAll(t => !(bool)t["ai"]);
            if (targets.Count > 0) menu.Items.Add(new ToolStripSeparator());
            foreach (var t in ai.GetRange(0, Math.Min(6, ai.Count))) menu.Items.Add(TargetItem(t, true));
            if (other.Count > 0)
            {
                var more = new ToolStripMenuItem(ai.Count > 0 ? "Send to another window" : "Send to…");
                foreach (var t in other) more.DropDownItems.Add(TargetItem(t, false));
                more.DropDown.Renderer = menu.Renderer;
                menu.Items.Add(more);
            }
            if (targets.Count == 0) menu.Items.Add(new ToolStripLabel("Open ChatGPT, Claude or another AI app to send") { ForeColor = Color.FromArgb(149, 144, 136) });

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Snip into draft", null, (s, e) => core.StartSnipFromUi());
            menu.Items.Add("Past chats", null, (s, e) => core.ShowView("history"));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Hide Chip", null, (s, e) => core.SetPetVisible(false));
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
            var at = PointToScreen(new Point((int)(petRect.X + petRect.Width / 2), (int)petRect.Y));
            menu.Show(at, ToolStripDropDownDirection.AboveLeft);
            ChipMenuRenderer.Round(menu);
        }

        ToolStripMenuItem TargetItem(Dictionary<string, object> t, bool primary)
        {
            string label = (string)t["label"];
            string title = (string)t["title"];
            string text = primary ? "Send to " + label : label;
            if (!title.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                string sub = title.Length > 34 ? title.Substring(0, 32) + "…" : title;
                text += "   " + sub;
            }
            long hwnd = long.Parse((string)t["hwnd"]);
            return new ToolStripMenuItem(text, null, (s, e) => core.SendCurrentTo(new IntPtr(hwnd)));
        }

        // ---------------- drawing ----------------

        void Render()
        {
            if (!IsHandleCreated || Width <= 0) return;
            var now = DateTime.UtcNow;
            puff += (targetPuff - puff) * 0.18f;
            if (now > nextBlink)
            {
                blinkUntil = now.AddMilliseconds(130);
                nextBlink = now.AddMilliseconds(2600 + rnd.Next(3200));
            }

            var pose = new MascotPose { Puff = puff, Holding = count > 0, Blink = now < blinkUntil };
            double secs = (now - started).TotalSeconds;
            float lift = 0;
            double sinceCatch = (now - catchAt).TotalMilliseconds;
            if (sinceCatch >= 0 && sinceCatch < 700)
            {
                double t = sinceCatch / 700.0;
                if (t < 0.55) lift = (float)(Math.Sin(t / 0.55 * Math.PI) * 9);
                else pose.Squash = (float)Math.Sin((t - 0.55) / 0.45 * Math.PI);
                pose.Mode = 3;
            }
            else if (mode == 2) { pose.Mode = 2; pose.Phase = (float)(secs / 0.9 % 1); }
            else if (hovering || dragOver || BubbleVisible) pose.Mode = 3;
            if (dragOver) lift = 4 + (float)Math.Sin(secs * 9) * 1.5f;

            bool busy = mode != 0 || hovering || dragOver || Math.Abs(puff - targetPuff) > 0.001f || sinceCatch < 900 || now < blinkUntil || BubbleVisible || (bubble != null && now < bubbleUntil.AddMilliseconds(400));
            // Chip only breathes while something is going on; at rest it holds still, and an
            // unchanged frame isn't redrawn, so an idle Chip costs next to nothing.
            float breathe = busy ? (float)Math.Sin(secs * 2 * Math.PI / 3.2) * 0.7f : 0f;
            timer.Interval = busy ? 33 : 200;
            string sig = busy ? null : count + "|" + pose.Blink + "|" + Math.Round(puff, 3) + "|" + Width + "|" + Left + "," + Top;
            if (sig != null && sig == lastFrame) return;
            lastFrame = sig;

            int w = Width, h = Height;
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    float pad = 8 * scale;
                    float size = petRect.Width - pad * 2;
                    float y = petRect.Y + pad - (lift + breathe) * size / 100f;

                    float sw = size * (0.42f - lift * 0.01f);
                    using (var sh = new SolidBrush(Color.FromArgb(46, 40, 24, 10)))
                        g.FillEllipse(sh, petRect.X + petRect.Width / 2f - sw / 2, petRect.Y + pad + size * 0.885f, sw, size * 0.075f);
                    Mascot.Draw(g, new RectangleF(petRect.X + pad, y, size, size), pose);
                    if (dragOver)
                        using (var ring = new Pen(Color.FromArgb(200, 184, 98, 43), 2f * scale) { DashStyle = DashStyle.Dash })
                            g.DrawEllipse(ring, petRect.X + 2 * scale, petRect.Y + 2 * scale, petRect.Width - 4 * scale, petRect.Height - 4 * scale);
                    if (count > 0) DrawBadge(g);
                    if (BubbleVisible) DrawBubble(g, now);
                }
                Push(bmp);
            }
        }

        string lastFrame;

        void DrawBubble(Graphics g, DateTime now)
        {
            // fade in quickly, fade out in the last 300 ms
            double inMs = (now - bubbleShown).TotalMilliseconds, outMs = (bubbleUntil - now).TotalMilliseconds;
            float a = (float)Math.Max(0, Math.Min(1, Math.Min(inMs / 160.0, outMs / 300.0)));
            if (a <= 0) return;
            float maxW = 230 * scale, padX = 13 * scale, padY = 9 * scale;
            using (var f = new Font("Segoe UI Semibold", 12.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fc = new Font("Segoe UI", 11.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var ts = g.MeasureString(bubble, f, (int)maxW);
                var cs = bubbleCta != null ? g.MeasureString(bubbleCta, fc, (int)maxW) : SizeF.Empty;
                float bw = Math.Max(ts.Width, cs.Width) + padX * 2;
                float bh = ts.Height + (bubbleCta != null ? cs.Height + 2 * scale : 0) + padY * 2;
                float tailY = petRect.Y + 26 * scale;
                float bx = bubbleLeft ? petRect.X - bw + 6 * scale : petRect.Right - 6 * scale;
                float by = Math.Max(2 * scale, tailY - bh + 10 * scale);
                bubbleRect = new RectangleF(bx, by, bw, bh);
                using (var path = RoundRect(bubbleRect, 14 * scale))
                {
                    // the little tail pointing at Chip
                    float tx = bubbleLeft ? bubbleRect.Right - 18 * scale : bubbleRect.Left + 18 * scale;
                    path.AddPolygon(new[] { new PointF(tx - 7 * scale, bubbleRect.Bottom - 1), new PointF(tx + 7 * scale, bubbleRect.Bottom - 1), new PointF(tx + (bubbleLeft ? 10 : -10) * scale, bubbleRect.Bottom + 9 * scale) });
                    using (var shadow = new SolidBrush(Color.FromArgb((int)(40 * a), 0, 0, 0)))
                    {
                        g.TranslateTransform(0, 2 * scale);
                        g.FillPath(shadow, path);
                        g.ResetTransform();
                    }
                    using (var b = new SolidBrush(Color.FromArgb((int)(248 * a), 29, 27, 24))) g.FillPath(b, path);
                }
                using (var tb = new SolidBrush(Color.FromArgb((int)(255 * a), 245, 243, 238)))
                    g.DrawString(bubble, f, tb, new RectangleF(bx + padX, by + padY, maxW, ts.Height + 2));
                if (bubbleCta != null)
                    using (var cb = new SolidBrush(Color.FromArgb((int)(255 * a), 240, 175, 125)))
                        g.DrawString(bubbleCta, fc, cb, new RectangleF(bx + padX, by + padY + ts.Height + 2 * scale, maxW, cs.Height + 2));
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void DrawBadge(Graphics g)
        {
            string text = count > 99 ? "99+" : count.ToString();
            using (var f = new Font("Segoe UI", 11.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var ts = g.MeasureString(text, f);
                float bh = 17 * scale, bw = Math.Max(bh, ts.Width + 9 * scale);
                var r = new RectangleF(petRect.Right - bw - 6 * scale, petRect.Y + 8 * scale, bw, bh);
                using (var path = new GraphicsPath())
                {
                    float d = bh;
                    path.AddArc(r.X, r.Y, d, d, 90, 180);
                    path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
                    path.CloseFigure();
                    using (var b = new SolidBrush(Color.FromArgb(240, 29, 28, 26))) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(255, 246, 245, 241), 1.6f * scale)) g.DrawPath(p, path);
                }
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, f, Brushes.White, new RectangleF(r.X, r.Y + 0.5f * scale, r.Width, r.Height), fmt);
            }
        }

        void Push(Bitmap bmp)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(mem, hbmp);
            try
            {
                var size = new SIZE { CX = bmp.Width, CY = bmp.Height };
                var src = new POINT();
                var dst = new POINT { X = Left, Y = Top };
                var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                if (!UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2) && !loggedFailure)
                {
                    loggedFailure = true;
                    Log.Error("Pet UpdateLayeredWindow failed: " + Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                SelectObject(mem, old);
                DeleteObject(hbmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Turns whatever was dropped (files, images, links, text) into draft content.</summary>
    static class PetDrop
    {
        public static bool CanAccept(IDataObject d)
        {
            return d != null && (d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent(DataFormats.Bitmap) || d.GetDataPresent("PNG")
                || d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent(DataFormats.Text) || d.GetDataPresent("UniformResourceLocatorW"));
        }
    }

    /// <summary>Chip's menu in the app's warm palette, with rounded Windows 11 corners.</summary>
    class ChipMenuRenderer : ToolStripProfessionalRenderer
    {
        static readonly Color Bg = Color.FromArgb(255, 255, 255);
        static readonly Color Hover = Color.FromArgb(241, 239, 233);
        static readonly Color Line = Color.FromArgb(230, 226, 217);
        static readonly Color Ink = Color.FromArgb(29, 27, 24);

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        public ChipMenuRenderer() : base(new Colors()) { RoundedEdges = true; }

        public static void Round(ToolStripDropDown d)
        {
            try { int round = 2; DwmSetWindowAttribute(d.Handle, 33, ref round, 4); } catch { }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
            using (var b = new SolidBrush(e.Item.Selected && e.Item.Enabled ? Hover : Bg)) e.Graphics.FillRectangle(b, r);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripMenuItem) e.TextColor = Ink;
            e.TextRectangle = new Rectangle(e.TextRectangle.X + 4, e.TextRectangle.Y, e.TextRectangle.Width, e.TextRectangle.Height);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Line)) e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(Line)) e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemBorder { get { return Hover; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Bg; } }
        }
    }
}
