using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Chipmunk
{
    /// <summary>The app icon (Chip's face on a warm tile) and .ico/.png export.</summary>
    static class Brand
    {
        public static Bitmap Draw(int s)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                using (var tile = RoundRect(new RectangleF(0, 0, s, s), s * 0.235f))
                using (var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(0, s), Color.FromArgb(250, 239, 225), Color.FromArgb(240, 220, 196)))
                    g.FillPath(brush, tile);

                // The head spans roughly x 25..75 and y 15..62 on the mascot grid.
                float scale = s <= 32 ? s / 50f : s / 58f;
                g.TranslateTransform(s / 2f, s / 2f + s * 0.04f);
                g.ScaleTransform(scale, scale);
                g.TranslateTransform(-50, -41);
                Mascot.DrawHead(g, 0.3f, false, 0);
            }
            return bmp;
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

        public static Icon MakeIcon(int size)
        {
            using (var ms = new MemoryStream())
            {
                WriteIco(ms, new[] { size });
                ms.Position = 0;
                return new Icon(ms, size, size);
            }
        }

        public static void WriteIco(string path)
        {
            using (var fs = File.Create(path)) WriteIco(fs, new[] { 16, 20, 24, 32, 40, 48, 64, 256 });
        }

        public static void WritePng(string path, int size)
        {
            using (var b = Draw(size)) b.Save(path, ImageFormat.Png);
        }

        /// <summary>Full-body mascot on a transparent background, for the UI and the README.</summary>
        public static void WriteMascotPng(string path, int size, bool holding)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    Mascot.Draw(g, new RectangleF(0, 0, size, size), new MascotPose { Holding = holding, Puff = holding ? 0.45f : 0.1f });
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        /// <summary>ICO with 32-bit DIB frames (and PNG for 256px), which every Windows API reads.</summary>
        static void WriteIco(Stream output, int[] sizes)
        {
            var frames = new List<byte[]>();
            foreach (var s in sizes)
            {
                using (var bmp = Draw(s)) frames.Add(s >= 256 ? Png(bmp) : Dib(bmp));
            }
            var w = new BinaryWriter(output);
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(frames[i].Length);
                w.Write(offset);
                offset += frames[i].Length;
            }
            foreach (var f in frames) w.Write(f);
            w.Flush();
        }

        static byte[] Png(Bitmap bmp)
        {
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }

        static byte[] Dib(Bitmap bmp)
        {
            int s = bmp.Width;
            using (var ms = new MemoryStream())
            {
                var w = new BinaryWriter(ms);
                w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
                w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                for (int y = s - 1; y >= 0; y--)
                    for (int x = 0; x < s; x++)
                    {
                        var c = bmp.GetPixel(x, y);
                        w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                    }
                int maskStride = ((s + 31) / 32) * 4;
                w.Write(new byte[maskStride * s]);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Debug: a contact sheet of icon sizes and pet poses, to review the artwork.</summary>
        public static void RenderSheet(string path)
        {
            int cell = 160;
            var poses = new List<KeyValuePair<string, MascotPose>>
            {
                new KeyValuePair<string, MascotPose>("idle", new MascotPose()),
                new KeyValuePair<string, MascotPose>("blink", new MascotPose { Blink = true }),
                new KeyValuePair<string, MascotPose>("holding", new MascotPose { Holding = true, Puff = 0.4f }),
                new KeyValuePair<string, MascotPose>("full", new MascotPose { Holding = true, Puff = 1f, Mode = 3 }),
                new KeyValuePair<string, MascotPose>("listening", new MascotPose { Mode = 1, Phase = 0.3f }),
                new KeyValuePair<string, MascotPose>("working", new MascotPose { Mode = 2, Phase = 0.4f, Holding = true, Puff = 0.5f }),
                new KeyValuePair<string, MascotPose>("catch", new MascotPose { Squash = 1f, Puff = 0.7f, Holding = true, Mode = 3 }),
            };
            using (var sheet = new Bitmap(cell * 4, cell * 3, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(246, 245, 241));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var f = new Font("Segoe UI", 10))
                {
                    for (int i = 0; i < poses.Count; i++)
                    {
                        int x = (i % 4) * cell, y = (i / 4) * cell;
                        Mascot.Draw(g, new RectangleF(x + 20, y + 10, 120, 120), poses[i].Value);
                        g.DrawString(poses[i].Key, f, Brushes.DimGray, x + 8, y + cell - 22);
                    }
                    int[] sizes = { 16, 24, 32, 48, 64, 128 };
                    int ix = 8;
                    foreach (var s in sizes)
                    {
                        using (var ic = Draw(s)) g.DrawImage(ic, ix, cell * 2 + 20, s, s);
                        ix += s + 14;
                    }
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }
    }
}
