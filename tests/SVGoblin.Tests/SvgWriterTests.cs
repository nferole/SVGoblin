using System.Drawing;
using System.Globalization;
using SVGoblin.Tests.Support;

namespace SVGoblin.Tests
{
    /// <summary>
    /// Serialization tests: exact path text, document structure, and immunity
    /// to the current culture (decimal commas would silently corrupt path data).
    /// </summary>
    public class SvgWriterTests
    {
        private static TracedLayer SampleLayer() => new(
            Color.FromArgb(0x12, 0x34, 0x56),
            new List<List<PathSegment>>
            {
                new()
                {
                    PathSegment.Line(new PointF(0, 0), new PointF(10, 0)),
                    PathSegment.Curve(new PointF(10, 0), new PointF(10.5f, 5.25f), new PointF(3.333f, 7), new PointF(0, 0)),
                },
            });

        [Fact]
        public void Write_LineAndCurve_ExactPathData()
        {
            string svg = SvgWriter.Write(new List<TracedLayer> { SampleLayer() }, 20, 20, new VectorizerOptions());

            Assert.Contains("d=\"M0,0 L10,0 C10.5,5.25 3.33,7 0,0 Z\"", svg);
            Assert.Contains("fill=\"#123456\"", svg);
            Assert.Contains("fill-rule=\"nonzero\"", svg);
        }

        [Fact]
        public void Write_ViewBoxAndDimensions()
        {
            string svg = SvgWriter.Write(new List<TracedLayer>(), 33, 47, new VectorizerOptions());

            var root = SvgParsing.Parse(svg).Root!;
            Assert.Equal("33", (string)root.Attribute("width")!);
            Assert.Equal("47", (string)root.Attribute("height")!);
            Assert.Equal(new RectangleF(0, 0, 33, 47), SvgParsing.ParseViewBox(svg));
        }

        [Fact]
        public void Write_BackgroundColorEmitsRect()
        {
            var options = new VectorizerOptions { BackgroundColor = "#ff0000" };
            string svg = SvgWriter.Write(new List<TracedLayer>(), 10, 10, options);

            var rects = SvgParsing.Parse(svg).Descendants().Where(e => e.Name.LocalName == "rect").ToList();
            var rect = Assert.Single(rects);
            Assert.Equal("#ff0000", (string)rect.Attribute("fill")!);
            Assert.Equal("10", (string)rect.Attribute("width")!);
            Assert.Equal("10", (string)rect.Attribute("height")!);
        }

        [Fact]
        public void Write_TwoLoops_TwoSubpathsInOnePathData()
        {
            var layer = new TracedLayer(Color.Black, new List<List<PathSegment>>
            {
                new()
                {
                    PathSegment.Line(new PointF(0, 0), new PointF(4, 0)),
                    PathSegment.Line(new PointF(4, 0), new PointF(4, 4)),
                },
                new()
                {
                    PathSegment.Line(new PointF(8, 8), new PointF(12, 8)),
                    PathSegment.Line(new PointF(12, 8), new PointF(12, 12)),
                },
            });

            string svg = SvgWriter.Write(new List<TracedLayer> { layer }, 16, 16, new VectorizerOptions());

            var d = Assert.Single(SvgParsing.PathData(svg));
            Assert.Equal("M0,0 L4,0 L4,4 Z M8,8 L12,8 L12,12 Z", d);
        }

        [Fact]
        public void Write_EmptyLayers_ValidMinimalSvg()
        {
            string svg = SvgWriter.Write(new List<TracedLayer>(), 8, 8, new VectorizerOptions());

            Assert.NotNull(SvgParsing.Parse(svg));
            Assert.Empty(SvgParsing.PathData(svg));
        }

        [Fact]
        public void Write_UnderSwedishCulture_SameOutputAsInvariant()
        {
            // sv-SE uses a decimal comma; if any number were formatted with the
            // current culture, path data like "10,5" vs "10.5" would corrupt
            // the geometry silently.
            var layers = new List<TracedLayer> { SampleLayer() };
            string invariant = SvgWriter.Write(layers, 20, 20, new VectorizerOptions());

            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
                string swedish = SvgWriter.Write(layers, 20, 20, new VectorizerOptions());

                // The literal pins the baseline: on a machine whose ambient
                // culture already uses decimal commas, comparing two
                // ambient-culture outputs would pass vacuously.
                Assert.Contains("C10.5,5.25 3.33,7 0,0", swedish);
                Assert.Equal(invariant, swedish);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
