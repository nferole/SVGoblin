using System.Drawing;
using System.Drawing.Imaging;

namespace SVGoblin.Tests.Support
{
    /// <summary>
    /// Synthetic inputs: Bitmaps for end-to-end tests (caller disposes) and raw
    /// ARGB pixel arrays for quantizer tests that should never touch GDI+.
    /// </summary>
    internal static class BitmapFactory
    {
        public static Bitmap Solid(int width, int height, Color color)
        {
            var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);
            g.Clear(color);
            return bmp;
        }

        public static Bitmap TwoToneSquare(int size, Color background, Color foreground, Rectangle square)
        {
            var bmp = Solid(size, size, background);
            FillRect(bmp, foreground, square);
            return bmp;
        }

        public static void FillRect(Bitmap bmp, Color color, Rectangle rect)
        {
            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                for (int x = rect.Left; x < rect.Right; x++)
                    bmp.SetPixel(x, y, color);
            }
        }

        public static Bitmap FilledCircle(int size, Color background, Color foreground, double radius)
        {
            var bmp = Solid(size, size, background);
            double c = (size - 1) / 2.0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double dx = x - c, dy = y - c;
                    if (dx * dx + dy * dy <= radius * radius)
                        bmp.SetPixel(x, y, foreground);
                }
            }

            return bmp;
        }

        public static Bitmap OnePixel(Color color)
        {
            var bmp = new Bitmap(1, 1);
            bmp.SetPixel(0, 0, color);
            return bmp;
        }

        public static Bitmap FullyTransparent(int width, int height)
            => new(width, height); // new Bitmap pixels default to transparent

        // GDI+ initializes its codec list lazily and not thread-safely:
        // concurrent first saves can resolve a null PNG encoder. Test classes
        // run in parallel, so all PNG saves are serialized through this lock.
        private static readonly object SaveLock = new();

        public static void SavePng(Bitmap bmp, string path)
        {
            lock (SaveLock)
                bmp.Save(path, ImageFormat.Png);
        }

        // --- Raw ARGB arrays (no GDI+) ---------------------------------------

        public static uint Argb(int a, int r, int g, int b)
            => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;

        public static uint[] SolidPixels(int width, int height, uint color)
        {
            var pixels = new uint[width * height];
            Array.Fill(pixels, color);
            return pixels;
        }

        /// <summary>Solid background with one filled rectangle of a different color.</summary>
        public static uint[] RectOnBackground(int width, int height, uint background, uint foreground, Rectangle rect)
        {
            var pixels = SolidPixels(width, height, background);
            FillRect(pixels, width, foreground, rect);
            return pixels;
        }

        public static void FillRect(uint[] pixels, int width, uint color, Rectangle rect)
        {
            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                for (int x = rect.Left; x < rect.Right; x++)
                    pixels[y * width + x] = color;
            }
        }
    }
}
