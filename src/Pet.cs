using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Chipmunk
{
    /// <summary>
    /// Chip, the desktop pet. A small always-on-top window you can drag anywhere: drop files,
    /// images, links or text on it to collect them; its cheeks fill up as the draft does; click
    /// to open Chipmunk. Drawn with per-pixel alpha so it has no box around it.
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

        const int BaseSize = 92;

        readonly AppCore core;
        readonly Timer timer = new Timer { Interval = 33 };
        readonly ToolTip tip = new ToolTip { InitialDelay = 600, ReshowDelay = 200 };
        readonly Random rnd = new Random();
        float scale = 1f;
        int count;
        float puff, targetPuff;
        int mode;                 // 0 idle, 1 listening, 2 working
        DateTime catchAt = DateTime.MinValue;
        DateTime nextBlink = DateTime.UtcNow.AddSeconds(3);
        DateTime blinkUntil = DateTime.MinValue;
        bool hovering, dragOver, suppressed, wanted;
        Point downAt, startLoc;
        bool mouseDown, dragging;
        DateTime started = DateTime.UtcNow;
        bool loggedFailure;

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
            MouseEnter += (s, e) => { hovering = true; };
            MouseLeave += (s, e) => { hovering = false; };
            DragEnter += (s, e) =>
            {
                e.Effect = PetDrop.CanAccept(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                dragOver = e.Effect != DragDropEffects.None;
            };
            DragLeave += (s, e) => { dragOver = false; };
            DragDrop += (s, e) =>
            {
                dragOver = false;
                core.AddFromDrop(e.Data, "Dropped on Chip");
            };
            ContextMenuStrip = BuildMenu();
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

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Open Chipmunk", null, (s, e) => core.ShowPanel());
            m.Items.Add("Talk", null, (s, e) => core.StartTalk());
            m.Items.Add("Snip into draft", null, (s, e) => core.StartSnipFromUi());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Hide Chip", null, (s, e) => core.SetPetVisible(false));
            return m;
        }

        public void Place(int[] saved)
        {
            if (!IsHandleCreated) CreateHandle();
            try { scale = Math.Max(1f, GetDpiForWindow(Handle) / 96f); } catch { scale = 1f; }
            int px = (int)(BaseSize * scale);
            Size = new Size(px, px);
            var wa = Screen.PrimaryScreen.WorkingArea;
            Point p = new Point(wa.Right - px - (int)(28 * scale), wa.Bottom - px - (int)(12 * scale));
            if (saved != null && saved.Length == 2)
            {
                var r = new Rectangle(saved[0], saved[1], px, px);
                foreach (var s in Screen.AllScreens)
                    if (s.WorkingArea.IntersectsWith(r)) { p = new Point(saved[0], saved[1]); break; }
            }
            Location = p;
        }

        /// <summary>Show or hide on request; "suppressed" hides it while a fullscreen app is in front.</summary>
        public void SetWanted(bool on)
        {
            wanted = on;
            ApplyVisibility();
        }

        public void SetSuppressed(bool on)
        {
            if (suppressed == on) return;
            suppressed = on;
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            bool show = wanted && !suppressed;
            if (show && !Visible)
            {
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

        public void SetCount(int n, string draftTitle)
        {
            count = Math.Max(0, n);
            targetPuff = Math.Min(1f, count / 6f);
            string t = string.IsNullOrWhiteSpace(draftTitle) ? "your draft" : "“" + draftTitle + "”";
            tip.SetToolTip(this, count == 0
                ? "Chip is ready. Drop files, images, links or text here, or click to open Chipmunk."
                : "Chip is carrying " + count + (count == 1 ? " thing" : " things") + " for " + t + ". Click to open.");
        }

        public void Catch() { catchAt = DateTime.UtcNow; }
        public void SetMode(int m) { mode = m; }

        void OnDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            mouseDown = true;
            dragging = false;
            downAt = Cursor.Position;
            startLoc = Location;
        }

        void OnMove(object s, MouseEventArgs e)
        {
            if (!mouseDown) return;
            var c = Cursor.Position;
            if (!dragging && (Math.Abs(c.X - downAt.X) > 4 || Math.Abs(c.Y - downAt.Y) > 4)) dragging = true;
            if (dragging) Location = new Point(startLoc.X + c.X - downAt.X, startLoc.Y + c.Y - downAt.Y);
        }

        void OnUp(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !mouseDown) return;
            mouseDown = false;
            if (dragging) core.SavePetPosition(new[] { Location.X, Location.Y });
            else core.TogglePanel();
            dragging = false;
        }

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

            var pose = new MascotPose { Puff = puff, Holding = count > 0, Blink = now < blinkUntil && mode == 0 };
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
            else if (mode == 1) { pose.Mode = 1; pose.Phase = (float)(secs / 1.1 % 1); }
            else if (mode == 2) { pose.Mode = 2; pose.Phase = (float)(secs / 0.9 % 1); }
            else if (hovering || dragOver) pose.Mode = 3;
            float breathe = (float)Math.Sin(secs * 2 * Math.PI / 3.2) * 0.7f;
            if (dragOver) lift = 4 + (float)Math.Sin(secs * 9) * 1.5f;

            int w = Width, h = Height;
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    float pad = 8 * scale;
                    float size = w - pad * 2;
                    float y = pad - (lift + breathe) * size / 100f;

                    // soft contact shadow, shrinking as Chip hops
                    float sw = size * (0.42f - lift * 0.01f);
                    using (var sh = new SolidBrush(Color.FromArgb(46, 40, 24, 10)))
                        g.FillEllipse(sh, w / 2f - sw / 2, pad + size * 0.885f, sw, size * 0.075f);

                    Mascot.Draw(g, new RectangleF(pad, y, size, size), pose);

                    if (dragOver)
                    {
                        using (var ring = new Pen(Color.FromArgb(200, 184, 98, 43), 2f * scale) { DashStyle = DashStyle.Dash })
                            g.DrawEllipse(ring, 2 * scale, 2 * scale, w - 4 * scale, h - 4 * scale);
                    }
                    if (count > 0) DrawBadge(g, w);
                }
                Push(bmp);
            }

            // Idle Chip only needs a few frames a second.
            bool busy = mode != 0 || hovering || dragOver || puff != targetPuff || sinceCatch < 900 || now < blinkUntil;
            timer.Interval = busy ? 33 : 90;
        }

        void DrawBadge(Graphics g, int w)
        {
            string text = count > 99 ? "99+" : count.ToString();
            using (var f = new Font("Segoe UI", 11.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var ts = g.MeasureString(text, f);
                float bh = 17 * scale, bw = Math.Max(bh, ts.Width + 9 * scale);
                var r = new RectangleF(w - bw - 6 * scale, 8 * scale, bw, bh);
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
            if (disposing) { timer.Dispose(); tip.Dispose(); }
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
}
