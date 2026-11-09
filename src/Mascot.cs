using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Chipmunk
{
    /// <summary>What the mascot is doing in a given frame.</summary>
    struct MascotPose
    {
        public float Puff;     // 0..1, how full the cheeks are
        public bool Blink;
        public bool Holding;   // holding an acorn: the draft has something in it
#pragma warning disable 0649
        public float Bob;      // vertical offset, in design units
#pragma warning restore 0649
        public float Squash;   // 0..1, squash on landing after a catch
        public int Mode;       // 0 idle, 1 listening, 2 working, 3 happy
        public float Phase;    // 0..1 animation phase for the current mode
    }

    /// <summary>
    /// Chip, the Chipmunk mascot, drawn with plain shapes on a 100x100 grid so it stays crisp
    /// at any size: the desktop pet, the app icon, and the illustrations all come from here.
    /// </summary>
    static class Mascot
    {
        static readonly Color Fur = Color.FromArgb(196, 122, 69);
        static readonly Color FurDark = Color.FromArgb(150, 86, 44);
        static readonly Color Stripe = Color.FromArgb(92, 50, 26);
        static readonly Color Cream = Color.FromArgb(246, 230, 210);
        static readonly Color CreamShade = Color.FromArgb(226, 196, 162);
        static readonly Color Ink = Color.FromArgb(32, 24, 18);
        static readonly Color EarInner = Color.FromArgb(236, 190, 160);
        static readonly Color Accent = Color.FromArgb(184, 98, 43);

        public static void Draw(Graphics g, RectangleF r, MascotPose p)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 100f, r.Height / 100f);

            // Squash and stretch around the feet when landing.
            float sq = p.Squash;
            g.TranslateTransform(50, 92);
            g.ScaleTransform(1 + sq * 0.10f, 1 - sq * 0.10f);
            g.TranslateTransform(-50, -92 + p.Bob);

            DrawTail(g);
            DrawBody(g, p);
            DrawHead(g, p.Puff, p.Blink, p.Mode == 1 ? 1 : p.Mode == 3 ? 2 : 0);

            if (p.Mode == 1) DrawSoundWaves(g, p.Phase);
            if (p.Mode == 2) DrawWorkingDots(g, p.Phase);

            g.Restore(state);
        }

        static void Fill(Graphics g, Color c, float cx, float cy, float rx, float ry)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - rx, cy - ry, rx * 2, ry * 2);
        }

        static void DrawTail(Graphics g)
        {
            using (var path = new GraphicsPath())
            {
                path.AddBezier(62, 86, 94, 88, 102, 50, 86, 26);
                path.AddBezier(86, 26, 80, 17, 68, 20, 70, 32);
                path.AddBezier(70, 32, 74, 50, 72, 68, 58, 78);
                path.CloseFigure();
                using (var b = new SolidBrush(FurDark)) g.FillPath(b, path);
            }
            // a lighter stripe down the tail
            using (var pen = new Pen(Color.FromArgb(120, Cream), 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(pen, 80, 30, 88, 44, 88, 62, 74, 76);
        }

        static void DrawBody(Graphics g, MascotPose p)
        {
            Fill(g, Fur, 50, 70, 22, 20);
            Fill(g, Cream, 50, 74, 13, 14.5f);
            Fill(g, FurDark, 39, 90, 7, 3.4f);
            Fill(g, FurDark, 61, 90, 7, 3.4f);
            if (p.Holding)
            {
                // an acorn held in both paws: this draft has things in it
                Fill(g, Color.FromArgb(176, 122, 58), 50, 70, 5.4f, 6.2f);
                using (var cap = new SolidBrush(Color.FromArgb(104, 66, 34)))
                {
                    g.FillPie(cap, 44.2f, 60.4f, 11.6f, 9f, 180, 180);
                    g.FillRectangle(cap, 49.3f, 59f, 1.4f, 3f);
                }
            }
            Fill(g, Color.FromArgb(178, 104, 54), 42.5f, 68, 4.4f, 5.2f);
            Fill(g, Color.FromArgb(178, 104, 54), 57.5f, 68, 4.4f, 5.2f);
        }

        /// <summary>The head on its own (used for the app icon too). mouth: 0 smile, 1 open, 2 big smile.</summary>
        public static void DrawHead(Graphics g, float puff, bool blink, int mouth)
        {
            // ears
            Fill(g, Fur, 33, 23, 7.5f, 7.5f);
            Fill(g, Fur, 67, 23, 7.5f, 7.5f);
            Fill(g, EarInner, 33, 24, 4.2f, 4.2f);
            Fill(g, EarInner, 67, 24, 4.2f, 4.2f);

            // head
            Fill(g, Fur, 50, 40, 21.5f, 19.5f);

            // forehead stripe, the chipmunk signature
            using (var pen = new Pen(Stripe, 4.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, 50, 23.5f, 50, 33);
            using (var pen = new Pen(Color.FromArgb(200, Cream), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, 35.5f, 30.5f, 10, 6, 200, 120);
                g.DrawArc(pen, 54.5f, 30.5f, 10, 6, 220, 120);
            }

            // muzzle and cheeks (they puff up as the draft fills)
            float cr = 8.2f + Clamp01(puff) * 5.2f;
            Fill(g, CreamShade, 36.5f, 49.5f, cr + 0.8f, cr * 0.92f + 0.8f);
            Fill(g, CreamShade, 63.5f, 49.5f, cr + 0.8f, cr * 0.92f + 0.8f);
            Fill(g, Cream, 36.5f, 49f, cr, cr * 0.92f);
            Fill(g, Cream, 63.5f, 49f, cr, cr * 0.92f);
            Fill(g, Cream, 50, 48, 11, 8.5f);

            // eyes
            if (blink)
            {
                using (var pen = new Pen(Ink, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(pen, 38.2f, 36.2f, 7.6f, 4, 0, 180);
                    g.DrawArc(pen, 54.2f, 36.2f, 7.6f, 4, 0, 180);
                }
            }
            else
            {
                Fill(g, Ink, 42, 38.5f, 3.8f, 4.1f);
                Fill(g, Ink, 58, 38.5f, 3.8f, 4.1f);
                Fill(g, Color.White, 43.3f, 37.1f, 1.3f, 1.3f);
                Fill(g, Color.White, 59.3f, 37.1f, 1.3f, 1.3f);
            }

            // nose and mouth
            Fill(g, Color.FromArgb(70, 40, 24), 50, 45.2f, 2.7f, 2.1f);
            using (var pen = new Pen(Color.FromArgb(70, 40, 24), 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (mouth == 1)
                {
                    Fill(g, Color.FromArgb(110, 52, 40), 50, 50.4f, 2.4f, 2.2f);
                }
                else
                {
                    float w = mouth == 2 ? 3.6f : 2.8f;
                    g.DrawArc(pen, 50 - w, 46.2f, w, mouth == 2 ? 4 : 3, 0, 180);
                    g.DrawArc(pen, 50, 46.2f, w, mouth == 2 ? 4 : 3, 0, 180);
                }
            }
        }

        static void DrawSoundWaves(Graphics g, float phase)
        {
            for (int i = 0; i < 2; i++)
            {
                float t = (phase + i * 0.5f) % 1f;
                int alpha = (int)(230 * (1 - t));
                float rr = 6 + t * 9;
                using (var pen = new Pen(Color.FromArgb(alpha, Accent), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(pen, 76 - rr, 26 - rr, rr * 2, rr * 2, -55, 70);
                    g.DrawArc(pen, 24 - rr, 26 - rr, rr * 2, rr * 2, 165, 70);
                }
            }
        }

        static void DrawWorkingDots(Graphics g, float phase)
        {
            for (int i = 0; i < 3; i++)
            {
                float t = (phase * 3 - i) % 3;
                float lift = t >= 0 && t < 1 ? (float)Math.Sin(t * Math.PI) * 3 : 0;
                Fill(g, Accent, 41 + i * 9, 8 - lift, 2.6f, 2.6f);
            }
        }

        static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    }
}
