using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Chipmunk
{
    /// <summary>A small notification in the corner that never steals focus from the app you're in.
    /// Matches the app's theme; click it to open Chipmunk.</summary>
    class Toast : Form
    {
        readonly Timer timer = new Timer();
        readonly Image logo;
        string text = "";
        bool error;
        float scale = 1f;
        UiKit.Palette pal = UiKit.Light;

        /// <summary>Set by the app so toasts follow the light/dark setting.</summary>
        public Func<bool> IsDark = () => false;

        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);

        public event EventHandler Activated2;

        public Toast(Image logo)
        {
            this.logo = logo;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Text = "Chipmunk notification";
            timer.Tick += (s, e) => { timer.Stop(); Hide(); };
            Click += (s, e) => { Hide(); Activated2?.Invoke(this, EventArgs.Empty); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /* NOACTIVATE */ | 0x00000080 /* TOOLWINDOW */ | 0x00000008 /* TOPMOST */;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW on Windows 10
                return cp;
            }
        }

        public void Show(string message, bool isError = false, int ms = 2800)
        {
            text = message ?? "";
            error = isError;
            pal = IsDark() ? UiKit.Dark : UiKit.Light;
            if (!IsHandleCreated) CreateHandle();
            try { scale = GetDpiForWindow(Handle) / 96f; } catch { scale = 1f; }
            if (scale <= 0) scale = 1f;

            int w = (int)(360 * scale);
            var textSize = TextRenderer.MeasureText(text, TextFont(), new Size(w - (int)(70 * scale), 1000), TextFormatFlags.WordBreak);
            int h = Math.Max((int)(60 * scale), textSize.Height + (int)(30 * scale));
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            Bounds = new Rectangle(screen.Right - w - (int)(16 * scale), screen.Bottom - h - (int)(16 * scale), w, h);
            BackColor = pal.Bg;
            Invalidate();
            if (!Visible) Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            Visible = true;
            UiKit.Elevate(Handle, pal);
            timer.Stop();
            timer.Interval = ms;
            timer.Start();
        }

        Font font;
        Font TextFont()
        {
            if (font == null || Math.Abs(font.Size - 13.5f * scale) > 0.1f) font = UiKit.Font(13.5f * scale);
            return font;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(pal.Bg);
            int pad = (int)(14 * scale);
            int icon = (int)(30 * scale);
            var ir = new Rectangle(pad, (Height - icon) / 2, icon, icon);
            if (logo != null) g.DrawImage(logo, ir);
            if (error)
                using (var b = new SolidBrush(pal.Danger))
                    g.FillEllipse(b, ir.Right - 9 * scale, ir.Y - 1 * scale, 10 * scale, 10 * scale);
            var rect = new Rectangle(ir.Right + (int)(12 * scale), 0, Width - ir.Right - (int)(12 * scale) - pad, Height);
            TextRenderer.DrawText(g, text, TextFont(), rect, pal.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
