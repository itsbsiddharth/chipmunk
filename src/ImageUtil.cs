using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Wic = System.Windows.Media.Imaging;

namespace Chipmunk
{
    static class ImageUtil
    {
        static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff" };

        public static bool IsImageFile(string path)
        {
            return Array.IndexOf(ImageExts, Path.GetExtension(path).ToLowerInvariant()) >= 0;
        }

        public static bool IsPng(byte[] d)
        {
            return d != null && d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47;
        }

        /// <summary>Decodes any image Windows can read. GDI+ covers the common formats;
        /// the Windows Imaging Component handles WebP/HEIC when their codecs are installed.</summary>
        public static Bitmap Decode(byte[] data)
        {
            try
            {
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch (ArgumentException) { }
            using (var ms = new MemoryStream(data))
            {
                var dec = Wic.BitmapDecoder.Create(ms, Wic.BitmapCreateOptions.None, Wic.BitmapCacheOption.OnLoad);
                var enc = new Wic.PngBitmapEncoder();
                enc.Frames.Add(Wic.BitmapFrame.Create(dec.Frames[0]));
                using (var png = new MemoryStream())
                {
                    enc.Save(png);
                    png.Position = 0;
                    using (var img = Image.FromStream(png))
                        return new Bitmap(img);
                }
            }
        }

        public static Size SizeOf(byte[] data)
        {
            try
            {
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms, false, false))
                    return img.Size;
            }
            catch
            {
                try { using (var b = Decode(data)) return b.Size; }
                catch { return Size.Empty; }
            }
        }

        public static byte[] EncodePng(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        public static byte[] ToPng(byte[] data)
        {
            if (IsPng(data)) return data;
            using (var b = Decode(data)) return EncodePng(b);
        }

        /// <summary>PNG bytes no larger than maxEdge on the long side (AI apps downscale anyway).</summary>
        public static byte[] PngForPaste(byte[] data, bool shrink, int maxEdge = 2000)
        {
            if (!shrink) return ToPng(data);
            using (var src = Decode(data))
            {
                int longEdge = Math.Max(src.Width, src.Height);
                if (longEdge <= maxEdge) return IsPng(data) ? data : EncodePng(src);
                double s = (double)maxEdge / longEdge;
                using (var dst = Resize(src, (int)Math.Round(src.Width * s), (int)Math.Round(src.Height * s)))
                    return EncodePng(dst);
            }
        }

        public static Bitmap Resize(Image src, int w, int h)
        {
            var dst = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(src, 0, 0, dst.Width, dst.Height);
            }
            return dst;
        }

        /// <summary>Small JPEG data URL for list previews.</summary>
        public static string ThumbDataUrl(byte[] data, int maxW, int maxH)
        {
            try
            {
                using (var src = Decode(data))
                {
                    double s = Math.Min(1.0, Math.Min((double)maxW / src.Width, (double)maxH / src.Height));
                    using (var t = Resize(src, (int)(src.Width * s), (int)(src.Height * s)))
                    using (var ms = new MemoryStream())
                    {
                        var codec = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatID == ImageFormat.Jpeg.Guid);
                        var p = new EncoderParameters(1);
                        p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 78L);
                        using (var flat = new Bitmap(t.Width, t.Height))
                        {
                            using (var g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImage(t, 0, 0); }
                            flat.Save(ms, codec, p);
                        }
                        return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch { return null; }
        }

        public static string ExtensionFor(string mediaType, byte[] data)
        {
            if (IsPng(data)) return ".png";
            switch ((mediaType ?? "").ToLowerInvariant())
            {
                case "image/jpeg": case "image/jpg": return ".jpg";
                case "image/gif": return ".gif";
                case "image/webp": return ".webp";
                case "image/bmp": return ".bmp";
                default: return ".png";
            }
        }
    }
}
