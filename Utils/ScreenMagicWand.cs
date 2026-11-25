using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScienceWordOverlay.Utils
{
    public class MagicFillResult
    {
        public BitmapSource Bitmap { get; set; } = null!;
        public Rect BoundsDip { get; set; }
    }

    public static class ScreenMagicWand
    {
        private const int SRCCOPY = 0x00CC0020;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
            [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        public static MagicFillResult? CaptureAndFloodFill(
            Point clickScreenDip,
            double dpiScaleX,
            double dpiScaleY,
            Color fillColor,
            double opacity,
            string pattern,
            int tolerance = 32)
        {
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (vw <= 0 || vh <= 0) return null;

            int sx = (int)Math.Round(clickScreenDip.X * dpiScaleX) - vx;
            int sy = (int)Math.Round(clickScreenDip.Y * dpiScaleY) - vy;
            if (sx < 0 || sy < 0 || sx >= vw || sy >= vh) return null;

            byte[] pixels = CaptureRegionBgra(vx, vy, vw, vh);
            if (pixels.Length == 0) return null;

            int stride = vw * 4;
            int seedIdx = sy * stride + sx * 4;
            byte sb = pixels[seedIdx];
            byte sg = pixels[seedIdx + 1];
            byte sr = pixels[seedIdx + 2];

            var visited = new byte[vw * vh];
            var queue = new Queue<int>(4096);
            int startPos = sy * vw + sx;
            visited[startPos] = 1;
            queue.Enqueue(startPos);

            int minX = sx, maxX = sx, minY = sy, maxY = sy;
            int maxPixels = Math.Min(vw * vh, 900_000);
            int count = 0;

            while (queue.Count > 0)
            {
                int pos = queue.Dequeue();
                int x = pos % vw;
                int y = pos / vw;
                count++;
                if (count > maxPixels) return null;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                if (x > 0) TryEnqueue(x - 1, y);
                if (x + 1 < vw) TryEnqueue(x + 1, y);
                if (y > 0) TryEnqueue(x, y - 1);
                if (y + 1 < vh) TryEnqueue(x, y + 1);
            }

            void TryEnqueue(int nx, int ny)
            {
                int nPos = ny * vw + nx;
                if (visited[nPos] != 0) return;
                int idx = ny * stride + nx * 4;
                if (Math.Abs(pixels[idx] - sb) <= tolerance &&
                    Math.Abs(pixels[idx + 1] - sg) <= tolerance &&
                    Math.Abs(pixels[idx + 2] - sr) <= tolerance)
                {
                    visited[nPos] = 1;
                    queue.Enqueue(nPos);
                }
            }

            int bw = maxX - minX + 1;
            int bh = maxY - minY + 1;
            if (bw < 3 || bh < 3) return null;

            byte[] outBuf = new byte[bw * bh * 4];
            byte aVal = (byte)Math.Clamp(opacity * 255.0, 20, 245);
            byte aLine = (byte)Math.Clamp((opacity + 0.35) * 255.0, 60, 255);
            byte rVal = fillColor.R, gVal = fillColor.G, bVal = fillColor.B;

            for (int y = 0; y < bh; y++)
            {
                int srcRow = (minY + y) * vw;
                int dstRow = y * bw * 4;
                for (int x = 0; x < bw; x++)
                {
                    if (visited[srcRow + minX + x] == 0) continue;

                    bool drawPixel = true;
                    byte alpha = aVal;

                    if (pattern == "hatch") { drawPixel = ((x + y) % 9) < 2; alpha = aLine; }
                    else if (pattern == "cross") { drawPixel = ((x + y) % 10) < 2 || (((x - y) % 10 + 10) % 10) < 2; alpha = aLine; }
                    else if (pattern == "dots") { int mx = x % 9, my = y % 9; drawPixel = (mx >= 3 && mx <= 5 && my >= 3 && my <= 5); alpha = aLine; }

                    if (!drawPixel) continue;

                    int oIdx = dstRow + x * 4;
                    double af = alpha / 255.0;
                    outBuf[oIdx] = (byte)(bVal * af);
                    outBuf[oIdx + 1] = (byte)(gVal * af);
                    outBuf[oIdx + 2] = (byte)(rVal * af);
                    outBuf[oIdx + 3] = alpha;
                }
            }

            var bmp = BitmapSource.Create(bw, bh, 96.0 * dpiScaleX, 96.0 * dpiScaleY, PixelFormats.Pbgra32, null, outBuf, bw * 4);
            bmp.Freeze();

            return new MagicFillResult
            {
                Bitmap = bmp,
                BoundsDip = new Rect((vx + minX) / dpiScaleX, (vy + minY) / dpiScaleY, bw / dpiScaleX, bh / dpiScaleY)
            };
        }

        // Chụp màn hình vùng chữ nhật (như Snipping Tool Rectangular Snip)
        public static BitmapSource? CaptureRectSnip(Rect rectDip, double dpiScaleX, double dpiScaleY)
        {
            int x = (int)Math.Round(rectDip.Left * dpiScaleX);
            int y = (int)Math.Round(rectDip.Top * dpiScaleY);
            int w = Math.Max(2, (int)Math.Round(rectDip.Width * dpiScaleX));
            int h = Math.Max(2, (int)Math.Round(rectDip.Height * dpiScaleY));

            byte[] buf = CaptureRegionBgra(x, y, w, h);
            if (buf.Length == 0) return null;
            for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;

            var bmp = BitmapSource.Create(w, h, 96.0 * dpiScaleX, 96.0 * dpiScaleY, PixelFormats.Pbgra32, null, buf, w * 4);
            bmp.Freeze();
            return bmp;
        }

        // Chụp màn hình vùng tự do (như Snipping Tool Freeform Snip - nền ngoài trong suốt)
        public static BitmapSource? CaptureLassoSnip(List<Point> lassoDip, double dpiScaleX, double dpiScaleY)
        {
            if (lassoDip == null || lassoDip.Count < 3) return null;
            double minX = lassoDip.Min(p => p.X), minY = lassoDip.Min(p => p.Y);
            double maxX = lassoDip.Max(p => p.X), maxY = lassoDip.Max(p => p.Y);
            var boundsDip = new Rect(minX, minY, Math.Max(2, maxX - minX), Math.Max(2, maxY - minY));

            var fullRectBmp = CaptureRectSnip(boundsDip, dpiScaleX, dpiScaleY);
            if (fullRectBmp == null) return null;

            int w = fullRectBmp.PixelWidth, h = fullRectBmp.PixelHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var clipGeo = new StreamGeometry();
                using (var ctx = clipGeo.Open())
                {
                    ctx.BeginFigure(new Point((lassoDip[0].X - minX) * dpiScaleX, (lassoDip[0].Y - minY) * dpiScaleY), true, true);
                    for (int i = 1; i < lassoDip.Count; i++)
                        ctx.LineTo(new Point((lassoDip[i].X - minX) * dpiScaleX, (lassoDip[i].Y - minY) * dpiScaleY), true, false);
                }
                clipGeo.Freeze();
                dc.PushClip(clipGeo);
                dc.DrawImage(fullRectBmp, new Rect(0, 0, w, h));
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(w, h, 96.0 * dpiScaleX, 96.0 * dpiScaleY, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static byte[] CaptureRegionBgra(int sx, int sy, int w, int h)
        {
            IntPtr hScreen = GetDC(IntPtr.Zero);
            IntPtr hMem = CreateCompatibleDC(hScreen);
            IntPtr hBmp = CreateCompatibleBitmap(hScreen, w, h);
            IntPtr hOld = SelectObject(hMem, hBmp);

            try
            {
                BitBlt(hMem, 0, 0, w, h, hScreen, sx, sy, SRCCOPY);

                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = w;
                bmi.bmiHeader.biHeight = -h;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;

                byte[] buf = new byte[w * h * 4];
                GetDIBits(hMem, hBmp, 0, (uint)h, buf, ref bmi, 0);
                return buf;
            }
            finally
            {
                SelectObject(hMem, hOld);
                DeleteObject(hBmp);
                DeleteDC(hMem);
                ReleaseDC(IntPtr.Zero, hScreen);
            }
        }
    }
}
