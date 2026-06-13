using System.Drawing;
using SVGoblin.Tests.Support;

namespace SVGoblin.Tests
{
    /// <summary>
    /// Divergence tests for color quantization on raw ARGB arrays (no GDI+):
    /// layer colors must stay valid through the cluster-mean divisions and
    /// merge chains, masks must match the input, and output must be stable.
    /// </summary>
    public class ColorQuantizerTests
    {
        private static readonly uint Blue = BitmapFactory.Argb(255, 0, 0, 255);
        private static readonly uint Red = BitmapFactory.Argb(255, 255, 0, 0);
        private static readonly uint White = BitmapFactory.Argb(255, 255, 255, 255);
        private static readonly uint Transparent = 0;

        [Fact]
        public void Quantize_RedSquareOnBlueBackground_OneLayerExactMask()
        {
            const int size = 16;
            var square = new Rectangle(4, 4, 8, 8);
            var pixels = BitmapFactory.RectOnBackground(size, size, Blue, Red, square);

            var layers = ColorQuantizer.Quantize(pixels, size, size, new VectorizerOptions());

            var layer = Assert.Single(layers);
            Assert.Equal(64, layer.PixelCount);
            Assert.Equal(Color.FromArgb(255, 0, 0).ToArgb(), layer.Color.ToArgb());
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    Assert.Equal(square.Contains(x, y) ? 1 : 0, layer.Mask[y * size + x]);
            }
        }

        [Fact]
        public void Quantize_NonSquareImage_ExactMask()
        {
            // Width != height with an off-center rect: an x/y swap anywhere in
            // the y * width + x indexing would scramble this mask.
            const int width = 24, height = 10;
            var rect = new Rectangle(4, 3, 12, 5);
            var pixels = BitmapFactory.RectOnBackground(width, height, Blue, Red, rect);

            var layers = ColorQuantizer.Quantize(pixels, width, height, new VectorizerOptions());

            var layer = Assert.Single(layers);
            Assert.Equal(60, layer.PixelCount);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    Assert.Equal(rect.Contains(x, y) ? 1 : 0, layer.Mask[y * width + x]);
            }
        }

        [Fact]
        public void Quantize_TransparentCorners_ForegroundStillFound()
        {
            // Transparent corners mean no background color: every opaque pixel
            // must still land in a layer instead of being dropped.
            const int size = 8;
            var pixels = BitmapFactory.SolidPixels(size, size, Transparent);
            BitmapFactory.FillRect(pixels, size, Red, new Rectangle(3, 3, 3, 3));

            var layers = ColorQuantizer.Quantize(pixels, size, size, new VectorizerOptions());

            var layer = Assert.Single(layers);
            Assert.Equal(9, layer.PixelCount);
        }

        [Fact]
        public void Quantize_FullyTransparent_EmptyList()
        {
            var pixels = BitmapFactory.SolidPixels(8, 8, Transparent);
            Assert.Empty(ColorQuantizer.Quantize(pixels, 8, 8, new VectorizerOptions()));
        }

        [Fact]
        public void Quantize_1x1Opaque_EmptyListNoCrash()
        {
            // The corner-sampled background of a 1x1 image is its only pixel,
            // so nothing remains as foreground. Documents current behavior.
            var pixels = new[] { BitmapFactory.Argb(255, 200, 100, 50) };
            Assert.Empty(ColorQuantizer.Quantize(pixels, 1, 1, new VectorizerOptions()));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(8)]
        public void Quantize_ManyColors_LayerCountAtMostMaxColors(int maxColors)
        {
            // Four well-separated stripes inside a white border, the merge
            // loops must respect the palette cap without dropping any stripe
            // pixel: every stripe color stays nearer some merged palette mean
            // than the white background.
            const int size = 32;
            var pixels = BitmapFactory.SolidPixels(size, size, White);
            uint[] stripeColors =
            {
                BitmapFactory.Argb(255, 255, 0, 0),
                BitmapFactory.Argb(255, 0, 255, 0),
                BitmapFactory.Argb(255, 0, 0, 255),
                BitmapFactory.Argb(255, 255, 255, 0),
            };
            for (int s = 0; s < 4; s++)
                BitmapFactory.FillRect(pixels, size, stripeColors[s], new Rectangle(4 + s * 6, 4, 6, 24));

            var layers = ColorQuantizer.Quantize(pixels, size, size, new VectorizerOptions { MaxColors = maxColors });

            // Well-separated stripes never merge voluntarily, so the count is
            // exact: all four survive until the cap forces merges.
            Assert.Equal(Math.Min(4, maxColors), layers.Count);
            const int stripePixels = 4 * 6 * 24;
            Assert.Equal(stripePixels, layers.Sum(l => l.PixelCount));
        }

        [Fact]
        public void Quantize_AntiAliasedRamp_AbsorbedIntoNearestLayerNoExtraLayers()
        {
            // Two flat regions joined by a 4-column red-to-blue ramp, the shape
            // of an anti-aliased edge. Ramp pixels sit on a color gradient, so
            // the flat-neighborhood gate must keep them out of the palette and
            // the assignment pass must absorb each one into the nearest layer
            // instead of growing extra layers or dropping pixels.
            const int size = 32;
            var pixels = BitmapFactory.SolidPixels(size, size, White);
            BitmapFactory.FillRect(pixels, size, Red, new Rectangle(4, 4, 10, 24));
            BitmapFactory.FillRect(pixels, size, Blue, new Rectangle(18, 4, 10, 24));
            for (int step = 0; step < 4; step++)
            {
                double t = (step + 1) / 5.0;
                uint ramp = BitmapFactory.Argb(255, (int)(255 * (1 - t)), 0, (int)(255 * t));
                BitmapFactory.FillRect(pixels, size, ramp, new Rectangle(14 + step, 4, 1, 24));
            }

            var layers = ColorQuantizer.Quantize(pixels, size, size, new VectorizerOptions());

            Assert.Equal(2, layers.Count);
            var red = Assert.Single(layers, l => l.Color.ToArgb() == Color.FromArgb(255, 0, 0).ToArgb());
            var blue = Assert.Single(layers, l => l.Color.ToArgb() == Color.FromArgb(0, 0, 255).ToArgb());

            // 240 flat pixels per region plus the 2 nearest ramp columns each.
            Assert.Equal(288, red.PixelCount);
            Assert.Equal(288, blue.PixelCount);
            for (int i = 0; i < pixels.Length; i++)
                Assert.True(red.Mask[i] + blue.Mask[i] <= 1, $"Pixel {i} is assigned to both layers");
        }

        [Fact]
        public void Quantize_ColorToleranceZero_NoCrash()
        {
            const int size = 16;
            var pixels = BitmapFactory.RectOnBackground(size, size, Blue, Red, new Rectangle(4, 4, 8, 8));

            var layers = ColorQuantizer.Quantize(pixels, size, size, new VectorizerOptions { ColorTolerance = 0 });

            Assert.NotEmpty(layers);
            Assert.All(layers, l => Assert.True(l.PixelCount >= 1));
        }

        [Fact]
        public void Quantize_MinShapeAreaBoundary_ExactCountKeptOneAboveRemoved()
        {
            const int size = 16;
            var pixels = BitmapFactory.RectOnBackground(size, size, Blue, Red, new Rectangle(7, 7, 2, 2));

            // The filter is strict (<): a 4-pixel layer survives MinShapeArea 4 and is removed at 5.
            var kept = ColorQuantizer.Quantize(pixels, size, size, new VectorizerOptions { MinShapeArea = 4 });
            var removed = ColorQuantizer.Quantize(pixels, size, size, new VectorizerOptions { MinShapeArea = 5 });

            var layer = Assert.Single(kept);
            Assert.Equal(4, layer.PixelCount);
            Assert.Empty(removed);
        }

        [Fact]
        public void Quantize_SameInputTwice_IdenticalLayers()
        {
            const int size = 32;
            var pixels = BitmapFactory.SolidPixels(size, size, White);
            BitmapFactory.FillRect(pixels, size, Red, new Rectangle(4, 4, 10, 24));
            BitmapFactory.FillRect(pixels, size, Blue, new Rectangle(18, 4, 10, 24));

            var first = ColorQuantizer.Quantize(pixels, size, size, new VectorizerOptions());
            var second = ColorQuantizer.Quantize(pixels, size, size, new VectorizerOptions());

            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Color.ToArgb(), second[i].Color.ToArgb());
                Assert.Equal(first[i].PixelCount, second[i].PixelCount);
                Assert.Equal(first[i].Mask, second[i].Mask);
            }
        }
    }
}
