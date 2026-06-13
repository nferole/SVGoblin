using System.Drawing;
using SVGoblin.Tests.Support;

namespace SVGoblin.Tests
{
    /// <summary>
    /// End-to-end divergence tests through the public Vectorizer API: emitted
    /// SVG must contain only finite numbers, stay inside the viewBox, be
    /// deterministic, and the three curve modes must agree on the geometry.
    /// </summary>
    public class VectorizerTests
    {
        private static VectorizerOptions Options(string mode) => mode switch
        {
            "schneider" => new VectorizerOptions { CurveMode = CurveMode.Schneider },
            "catmullrom" => new VectorizerOptions { CurveMode = CurveMode.CatmullRom },
            "lines" => new VectorizerOptions { EnableCurveFitting = false },
            _ => throw new ArgumentException(mode),
        };

        private static string VectorizeCircle(string mode, out VectorizeStats? stats)
        {
            using var bmp = BitmapFactory.FilledCircle(64, Color.White, Color.Red, 24);
            var vectorizer = new Vectorizer(Options(mode));
            string svg = vectorizer.Vectorize(bmp);
            stats = vectorizer.LastStats;
            return svg;
        }

        [Theory]
        [InlineData("schneider")]
        [InlineData("catmullrom")]
        [InlineData("lines")]
        public void Vectorize_Circle64_NoNonFiniteNumbers(string mode)
        {
            string svg = VectorizeCircle(mode, out _);
            SvgParsing.AssertNoNonFiniteTokens(svg);
        }

        [Theory]
        [InlineData("schneider")]
        [InlineData("catmullrom")]
        [InlineData("lines")]
        public void Vectorize_Circle64_GeometryWithinViewBoxMargin(string mode)
        {
            string svg = VectorizeCircle(mode, out _);
            var viewBox = SvgParsing.ParseViewBox(svg);

            foreach (var loop in SvgParsing.ParseLoops(svg))
            {
                // On-curve points must hug the image; control points get a 25%
                // margin and act as the explosion detector.
                foreach (var s in loop)
                {
                    var onCurve = RectangleF.Inflate(viewBox, 1, 1);
                    Assert.True(onCurve.Contains(s.Start) && onCurve.Contains(s.End),
                        $"Endpoint outside viewBox+1: ({s.Start}, {s.End})");
                }

                GeometryAsserts.AssertWithinBounds(loop, viewBox, viewBox.Width * 0.25f);
            }
        }

        [Theory]
        [InlineData("schneider")]
        [InlineData("catmullrom")]
        [InlineData("lines")]
        public void Vectorize_SameBitmapTwice_IdenticalSvgString(string mode)
        {
            using var bmp = BitmapFactory.FilledCircle(64, Color.White, Color.Red, 24);
            string first = new Vectorizer(Options(mode)).Vectorize(bmp);
            string second = new Vectorizer(Options(mode)).Vectorize(bmp);

            Assert.Equal(first, second);
        }

        [Fact]
        public void Vectorize_CrossMode_OutlinesAgreeWithinBound()
        {
            // All three modes fit the same simplified polygon, so their
            // outlines may only differ by simplification + fit tolerance.
            var sampled = new Dictionary<string, List<PointF>>();
            foreach (var mode in new[] { "schneider", "catmullrom", "lines" })
            {
                var loops = SvgParsing.ParseLoops(VectorizeCircle(mode, out _));
                var loop = Assert.Single(loops);
                sampled[mode] = GeometryAsserts.Sample(loop, 64);
            }

            var options = new VectorizerOptions();
            double bound = options.SimplifyTolerance + options.CurveTolerance + 2;
            foreach (var (a, b) in new[] { ("schneider", "catmullrom"), ("schneider", "lines"), ("catmullrom", "lines") })
            {
                double deviation = GeometryAsserts.SymmetricMaxDeviation(sampled[a], sampled[b]);
                Assert.True(deviation <= bound,
                    $"{a} and {b} outlines diverge by {deviation} px (bound {bound})");
            }

            double expectedArea = Math.PI * 24 * 24;
            foreach (var (mode, points) in sampled)
            {
                double area = Math.Abs(GeometryAsserts.ShoelaceArea(points));
                Assert.True(Math.Abs(area - expectedArea) <= 0.15 * expectedArea,
                    $"{mode} outline area {area} diverges from circle area {expectedArea}");
            }
        }

        [Fact]
        public void Vectorize_NonSquareBitmap_ViewBoxMatchesAndGeometryInside()
        {
            // Width != height end to end: an x/y swap anywhere in the pipeline
            // would push the traced rect outside the 48x32 viewBox.
            using var bmp = BitmapFactory.Solid(48, 32, Color.White);
            BitmapFactory.FillRect(bmp, Color.Red, new Rectangle(8, 8, 24, 16));

            string svg = new Vectorizer().Vectorize(bmp);

            Assert.Equal(new RectangleF(0, 0, 48, 32), SvgParsing.ParseViewBox(svg));
            var loop = Assert.Single(SvgParsing.ParseLoops(svg));
            GeometryAsserts.AssertWithinBounds(loop, new RectangleF(8, 8, 24, 16), 1);
        }

        [Fact]
        public void Vectorize_NestedShapes_LargerLayerEmittedFirst()
        {
            // Layers are sorted by pixel count so smaller shapes draw on top;
            // in reversed order the blue square would hide the red one. The
            // red square must be at least 10x10: a smaller one has fewer than
            // MinClusterPixels flat-neighborhood pixels and is folded away.
            using var bmp = BitmapFactory.TwoToneSquare(32, Color.White, Color.Blue, new Rectangle(4, 4, 24, 24));
            BitmapFactory.FillRect(bmp, Color.Red, new Rectangle(11, 11, 10, 10));

            string svg = new Vectorizer().Vectorize(bmp);

            var fills = SvgParsing.Parse(svg).Descendants()
                .Where(e => e.Name.LocalName == "path")
                .Select(e => (string)e.Attribute("fill")!)
                .ToList();
            Assert.Equal(new[] { "#0000FF", "#FF0000" }, fills);
        }

        [Theory]
        [InlineData(120, 0)] // the cutoff is strict <, so gray 120 is background at threshold 120
        [InlineData(121, 1)] // only the darker blob
        [InlineData(136, 2)] // both blobs
        public void Vectorize_BlackWhiteThreshold_StrictCutoffSelectsBlobs(int threshold, int expectedLoops)
        {
            using var bmp = BitmapFactory.Solid(24, 24, Color.White);
            BitmapFactory.FillRect(bmp, Color.FromArgb(120, 120, 120), new Rectangle(4, 4, 6, 6));
            BitmapFactory.FillRect(bmp, Color.FromArgb(135, 135, 135), new Rectangle(14, 14, 6, 6));
            var options = new VectorizerOptions { Mode = VectorizeMode.BlackWhite, Threshold = threshold };

            var vectorizer = new Vectorizer(options);
            vectorizer.Vectorize(bmp);

            Assert.Equal(expectedLoops, vectorizer.LastStats!.Loops);
            Assert.Equal(expectedLoops == 0 ? 0 : 1, vectorizer.LastStats.Layers);
        }

        [Fact]
        public void Vectorize_BlackWhiteMode_SingleBlackPath()
        {
            using var bmp = BitmapFactory.FilledCircle(64, Color.White, Color.Black, 24);
            string svg = new Vectorizer(new VectorizerOptions { Mode = VectorizeMode.BlackWhite }).Vectorize(bmp);

            var paths = SvgParsing.Parse(svg).Descendants().Where(e => e.Name.LocalName == "path").ToList();
            var path = Assert.Single(paths);
            Assert.Equal("#000000", (string)path.Attribute("fill")!);
        }

        [Fact]
        public void Vectorize_1x1Bitmap_ValidSvgNoPaths()
        {
            using var bmp = BitmapFactory.OnePixel(Color.Red);
            var vectorizer = new Vectorizer();
            string svg = vectorizer.Vectorize(bmp);

            Assert.NotNull(SvgParsing.Parse(svg));
            Assert.Equal(0, vectorizer.LastStats!.Layers);
            Assert.Empty(SvgParsing.ParseLoops(svg));
        }

        [Fact]
        public void Vectorize_FullyTransparent_ValidSvgNoPaths()
        {
            using var bmp = BitmapFactory.FullyTransparent(16, 16);
            var vectorizer = new Vectorizer();
            string svg = vectorizer.Vectorize(bmp);

            Assert.NotNull(SvgParsing.Parse(svg));
            Assert.Equal(0, vectorizer.LastStats!.Layers);
            Assert.Empty(SvgParsing.ParseLoops(svg));
        }

        [Fact]
        public void Vectorize_MinShapeAreaFiltersSmallBlob()
        {
            // A 12x12 square plus a separate 2x2 blob of the same color: the
            // blob's traced loop (area exactly 4) must survive MinShapeArea 4
            // (strict <) and be dropped at 5. Smoothing and the default
            // simplify tolerance would erase the tiny loop before the area
            // filter is meaningful, so they are dialed down here.
            using var bmp = BitmapFactory.TwoToneSquare(32, Color.White, Color.Red, new Rectangle(4, 4, 12, 12));
            BitmapFactory.FillRect(bmp, Color.Red, new Rectangle(24, 24, 2, 2));

            VectorizerOptions Options(int minShapeArea) => new()
            {
                MinShapeArea = minShapeArea,
                ContourSmoothPasses = 0,
                SimplifyTolerance = 0.5,
                EnableCurveFitting = false,
            };

            var keepBoth = new Vectorizer(Options(4));
            keepBoth.Vectorize(bmp);
            Assert.Equal(2, keepBoth.LastStats!.Loops);

            var filtered = new Vectorizer(Options(5));
            filtered.Vectorize(bmp);
            Assert.Equal(1, filtered.LastStats!.Loops);
        }

        [Fact]
        public void Vectorize_SmoothPasses0Vs10_AreaWithin25Percent()
        {
            using var bmp = BitmapFactory.TwoToneSquare(32, Color.White, Color.Red, new Rectangle(8, 8, 16, 16));

            double AreaWithPasses(int passes)
            {
                var options = new VectorizerOptions { EnableCurveFitting = false, ContourSmoothPasses = passes };
                var loops = SvgParsing.ParseLoops(new Vectorizer(options).Vectorize(bmp));
                var loop = Assert.Single(loops);
                return Math.Abs(GeometryAsserts.ShoelaceArea(GeometryAsserts.Sample(loop, 16)));
            }

            double unsmoothed = AreaWithPasses(0);
            double smoothed = AreaWithPasses(10);

            Assert.True(smoothed >= 0.75 * unsmoothed && smoothed <= 1.05 * unsmoothed,
                $"Ten smoothing passes distorted the area from {unsmoothed} to {smoothed}");
        }

        [Fact]
        public void Vectorize_LastStats_Populated()
        {
            VectorizeCircle("schneider", out var stats);

            Assert.NotNull(stats);
            Assert.Equal(1, stats!.Layers);
            Assert.Equal(1, stats.Loops);
            Assert.True(stats.Segments > 0);
        }
    }
}
