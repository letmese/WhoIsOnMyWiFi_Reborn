using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace WhoIsOnMyWiFi_Reborn
{
    public static class IconGenerator
    {
        public static void GenerateAppIcon(string outputPath)
        {
            var sizes = new[] { 256, 48, 32, 16 };
            var bitmaps = new List<(int size, Bitmap bmp)>();

            foreach (var size in sizes)
            {
                bitmaps.Add((size, RenderIcon(size)));
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // 1. ICONDIR (6 bytes)
            bw.Write((ushort)0); // idReserved
            bw.Write((ushort)1); // idType = 1 (icon)
            bw.Write((ushort)sizes.Length); // idCount

            // Prepare frames
            var frames = new List<(int size, byte[] data, bool isPng)>();
            foreach (var (size, bmp) in bitmaps)
            {
                if (size == 256)
                {
                    using var pngMs = new MemoryStream();
                    bmp.Save(pngMs, ImageFormat.Png);
                    frames.Add((size, pngMs.ToArray(), true));
                }
                else
                {
                    byte[] dib = CreateDibFrame(bmp);
                    frames.Add((size, dib, false));
                }
            }

            // 2. ICONDIRENTRY array (16 bytes each)
            int offset = 6 + (16 * frames.Count);
            foreach (var (size, data, isPng) in frames)
            {
                bw.Write((byte)(size == 256 ? 0 : size)); // bWidth
                bw.Write((byte)(size == 256 ? 0 : size)); // bHeight
                bw.Write((byte)0);                       // bColorCount
                bw.Write((byte)0);                       // bReserved
                bw.Write((ushort)1);                     // wPlanes
                bw.Write((ushort)32);                    // wBitCount
                bw.Write((uint)data.Length);             // dwBytesInRes
                bw.Write((uint)offset);                  // dwImageOffset
                offset += data.Length;
            }

            // 3. Write Frame Data
            foreach (var (_, data, _) in frames)
            {
                bw.Write(data);
            }

            bw.Flush();
            File.WriteAllBytes(outputPath, ms.ToArray());

            // Dispose bitmaps
            foreach (var (_, bmp) in bitmaps)
            {
                bmp.Dispose();
            }
        }

        private static Bitmap RenderIcon(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float s = size / 256.0f;

            // 1. Rounded App Badge Background
            float cornerRadius = 48f * s;
            float pad = 10f * s;
            float rectSize = size - (pad * 2f);
            using (var path = GetRoundedRectPath(new RectangleF(pad, pad, rectSize, rectSize), cornerRadius))
            {
                using var bgBrush = new LinearGradientBrush(
                    new PointF(pad, pad),
                    new PointF(pad + rectSize, pad + rectSize),
                    Color.FromArgb(255, 14, 20, 32),
                    Color.FromArgb(255, 23, 33, 53));
                g.FillPath(bgBrush, path);

                using var borderPen = new Pen(Color.FromArgb(140, 0, 163, 255), 3f * s);
                g.DrawPath(borderPen, path);
            }

            // 2. Subtle Glow Radar Circle
            float centerX = size / 2f;
            float centerY = size * 0.62f;

            // 3. Concentric Wi-Fi Signal Arcs
            DrawWifiArc(g, centerX, centerY, 80f * s, 18f * s, Color.FromArgb(255, 0, 210, 255));
            DrawWifiArc(g, centerX, centerY, 54f * s, 16f * s, Color.FromArgb(255, 0, 140, 255));
            DrawWifiArc(g, centerX, centerY, 28f * s, 14f * s, Color.FromArgb(255, 56, 189, 248));

            // 4. Center Glowing Radar Pulse Dot
            float dotRadius = 14f * s;
            if (dotRadius < 2f) dotRadius = 2f;
            using (var dotBrush = new SolidBrush(Color.FromArgb(255, 0, 229, 255)))
            {
                g.FillEllipse(dotBrush, centerX - dotRadius, centerY - dotRadius, dotRadius * 2f, dotRadius * 2f);
            }
            using (var centerDot = new SolidBrush(Color.White))
            {
                float coreRadius = dotRadius * 0.5f;
                g.FillEllipse(centerDot, centerX - coreRadius, centerY - coreRadius, coreRadius * 2f, coreRadius * 2f);
            }

            // 5. Shield/Radar Accent Indicator (top right)
            if (size >= 32)
            {
                float shieldX = size - (55f * s);
                float shieldY = 22f * s;
                float shieldW = 28f * s;
                float shieldH = 34f * s;

                using var shieldPath = new GraphicsPath();
                shieldPath.AddLine(shieldX, shieldY, shieldX + shieldW, shieldY);
                shieldPath.AddLine(shieldX + shieldW, shieldY, shieldX + shieldW, shieldY + (shieldH * 0.55f));
                shieldPath.AddLine(shieldX + shieldW, shieldY + (shieldH * 0.55f), shieldX + (shieldW / 2f), shieldY + shieldH);
                shieldPath.AddLine(shieldX + (shieldW / 2f), shieldY + shieldH, shieldX, shieldY + (shieldH * 0.55f));
                shieldPath.CloseFigure();

                using var shieldBrush = new LinearGradientBrush(
                    new PointF(shieldX, shieldY),
                    new PointF(shieldX + shieldW, shieldY + shieldH),
                    Color.FromArgb(255, 16, 185, 129),
                    Color.FromArgb(255, 5, 150, 105));
                g.FillPath(shieldBrush, shieldPath);

                using var shieldPen = new Pen(Color.FromArgb(255, 110, 231, 183), 1.5f * s);
                g.DrawPath(shieldPen, shieldPath);
            }

            return bmp;
        }

        private static void DrawWifiArc(Graphics g, float cx, float cy, float radius, float strokeWidth, Color color)
        {
            if (radius <= 0) return;
            using var pen = new Pen(color, strokeWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            // 90-degree sweep from 225 deg to 315 deg (pointing upwards)
            g.DrawArc(pen, cx - radius, cy - radius, radius * 2f, radius * 2f, 225f, 90f);
        }

        private static GraphicsPath GetRoundedRectPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static byte[] CreateDibFrame(Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            int imageSize = width * height * 4;
            int andMaskRowBytes = ((width + 31) / 32) * 4;
            int andMaskSize = andMaskRowBytes * height;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // BITMAPINFOHEADER (40 bytes)
            bw.Write(40);                   // biSize
            bw.Write(width);                // biWidth
            bw.Write(height * 2);           // biHeight (XOR + AND mask height)
            bw.Write((ushort)1);            // biPlanes
            bw.Write((ushort)32);           // biBitCount
            bw.Write(0);                    // biCompression (BI_RGB)
            bw.Write(imageSize + andMaskSize); // biSizeImage
            bw.Write(0);                    // biXPelsPerMeter
            bw.Write(0);                    // biYPelsPerMeter
            bw.Write(0);                    // biClrUsed
            bw.Write(0);                    // biClrImportant

            // Pixel data: bottom-up BGRA
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    bw.Write(c.B);
                    bw.Write(c.G);
                    bw.Write(c.R);
                    bw.Write(c.A);
                }
            }

            // AND mask (all zeros for 32-bit ARGB)
            byte[] andMask = new byte[andMaskSize];
            bw.Write(andMask);

            bw.Flush();
            return ms.ToArray();
        }
    }
}
