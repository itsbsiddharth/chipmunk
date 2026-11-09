using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Chipmunk
{
    /// <summary>A small notification in the corner that never steals focus from the app you're in.</summary>
    class Toast : Form
    {
        readonly Timer timer = new Timer();
        readonly Image logo;
        string text = "";
        bool error;
        float scale = 1f;

        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);

        public event EventHandler Activated2;

        public Toast(Image logo)
        {
            this.logo = logo;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(30, 30, 36);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
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
                return cp;
            }
        }

        public void Show(string message, bool isError = false, int ms = 2800)
        {
            text = message ?? "";
            error = isError;
            if (!IsHandleCreated) CreateHandle();
            try { scale = GetDpiForWindow(Handle) / 96f; } catch { scale = 1f; }
            if (scale <= 0) scale = 1f;

            int w = (int)(360 * scale);
            var textSize = TextRenderer.MeasureText(text, TextFont(), new Size(w - (int)(64 * scale), 1000), TextFormatFlags.WordBreak);
            int h = Math.Max((int)(58 * scale), textSize.Height + (int)(28 * scale));
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            Bounds = new Rectangle(screen.Right - w - (int)(16 * scale), screen.Bottom - h - (int)(16 * scale), w, h);
            Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, w + 1, h + 1, (int)(16 * scale), (int)(16 * scale)));
            BackColor = error ? Color.FromArgb(122, 35, 35) : Color.FromArgb(30, 30, 36);
            Invalidate();
            if (!Visible) Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            Visible = true;
            timer.Stop();
            timer.Interval = ms;
            timer.Start();
        }

        Font font;
        Font TextFont()
        {
            if (font == null || Math.Abs(font.Size - 13.5f * scale) > 0.1f) font = new Font("Segoe UI", 13.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            return font;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int pad = (int)(14 * scale);
            int icon = (int)(28 * scale);
            if (logo != null) g.DrawImage(logo, pad, (Height - icon) / 2, icon, icon);
            var rect = new Rectangle(pad + icon + (int)(12 * scale), 0, Width - pad * 2 - icon - (int)(12 * scale), Height);
            TextRenderer.DrawText(g, text, TextFont(), rect, Color.FromArgb(242, 242, 246),
                TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
