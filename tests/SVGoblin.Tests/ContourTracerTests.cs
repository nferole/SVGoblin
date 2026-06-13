using System.Drawing;

namespace SVGoblin.Tests
{
    /// <summary>
    /// Divergence tests for contour tracing: traced loops must enclose exactly
    /// the masked pixels (signed areas), terminate on pathological masks, and
    /// give holes the opposite winding.
    /// </summary>
    public class ContourTracerTests
    {
        /// <summary>Builds a mask from rows of '0'/'1' characters.</summary>
        private static (byte[] Mask, int Width, int Height) Mask(params string[] rows)
        {
            int width = rows[0].Length;
            var mask = new byte[width * rows.Length];
            for (int y = 0; y < rows.Length; y++)
            {
                for (int x = 0; x < width; x++)
                    mask[y * width + x] = (byte)(rows[y][x] == '1' ? 1 : 0);
            }

            return (mask, width, rows.Length);
        }

        [Fact]
        public void Trace_SingleSquareMask_OneContourAreaExact()
        {
            var (mask, w, h) = Mask(
                "0000",
                "0110",
                "0110",
                "0000");

            var contours = ContourTracer.Trace(mask, w, h);

            Assert.Single(contours);
            Assert.Equal(4, Math.Abs(ContourTracer.SignedArea(contours[0])));
            Assert.Equal(8, contours[0].Count); // every unit step of the walk is kept
        }

        [Fact]
        public void Trace_RingMask_OuterAndHoleOppositeWinding()
        {
            var (mask, w, h) = Mask(
                "11111",
                "10001",
                "10001",
                "10001",
                "11111");

            var contours = ContourTracer.Trace(mask, w, h);

            Assert.Equal(2, contours.Count);
            double a0 = ContourTracer.SignedArea(contours[0]);
            double a1 = ContourTracer.SignedArea(contours[1]);

            // Opposite winding is what fill-rule="nonzero" relies on to render
            // holes; net enclosed area must equal the 16 set pixels.
            Assert.True(Math.Sign(a0) != Math.Sign(a1),
                $"Outer boundary and hole have the same winding (areas {a0} and {a1})");
            Assert.Equal(16, a0 + a1);
        }

        [Fact]
        public void Trace_EmptyMask_NoContours()
        {
            var (mask, w, h) = Mask("000", "000", "000");
            Assert.Empty(ContourTracer.Trace(mask, w, h));
        }

        [Fact]
        public void Trace_FullMask_SinglePerimeter()
        {
            var (mask, w, h) = Mask("1111", "1111", "1111");

            var contours = ContourTracer.Trace(mask, w, h);

            Assert.Single(contours);
            Assert.Equal(12, Math.Abs(ContourTracer.SignedArea(contours[0])));
            Assert.Equal(2 * (4 + 3), contours[0].Count);
        }

        [Fact]
        public void Trace_SinglePixel_FourPointLoopAreaOne()
        {
            var (mask, w, h) = Mask("000", "010", "000");

            var contours = ContourTracer.Trace(mask, w, h);

            Assert.Single(contours);
            Assert.Equal(4, contours[0].Count);
            Assert.Equal(1, Math.Abs(ContourTracer.SignedArea(contours[0])));
        }

        [Fact]
        public void Trace_Checkerboard_TerminatesAndAreasSumToPixelCount()
        {
            // Worst case for the 8-connected diagonal-junction rule and the
            // walk's loop guard: every interior lattice point is a junction.
            var rows = new string[6];
            for (int y = 0; y < 6; y++)
                rows[y] = string.Concat(Enumerable.Range(0, 6).Select(x => (x + y) % 2 == 0 ? '1' : '0'));
            var (mask, w, h) = Mask(rows);
            int setPixels = mask.Count(b => b == 1);

            var contours = ContourTracer.Trace(mask, w, h);

            Assert.NotEmpty(contours);
            Assert.All(contours, c => Assert.True(c.Count >= 4, $"Degenerate loop with {c.Count} points"));
            Assert.Equal(setPixels, contours.Sum(ContourTracer.SignedArea));
        }

        [Fact]
        public void Trace_TwoSeparateRegions_TwoContours()
        {
            var (mask, w, h) = Mask(
                "000000",
                "010010",
                "000000");

            var contours = ContourTracer.Trace(mask, w, h);

            Assert.Equal(2, contours.Count);
            Assert.All(contours, c => Assert.Equal(1, Math.Abs(ContourTracer.SignedArea(c))));
        }

        [Fact]
        public void SignedArea_LargeCoordinates_NoOverflow()
        {
            const int c = 1_000_000_000;
            var square = new List<Point> { new(-c, -c), new(c, -c), new(c, c), new(-c, c) };

            // 4e18: near long.MaxValue in the shoelace accumulator; the long
            // arithmetic must not wrap.
            Assert.Equal(4e18, Math.Abs(ContourTracer.SignedArea(square)));
        }

        [Fact]
        public void Trace_SameMaskTwice_IdenticalContours()
        {
            var (mask, w, h) = Mask(
                "00000",
                "01110",
                "01010",
                "01110",
                "00000");

            var first = ContourTracer.Trace(mask, w, h);
            var second = ContourTracer.Trace(mask, w, h);

            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
                Assert.Equal(first[i], second[i]);
        }
    }
}
