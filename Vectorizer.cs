using System.Drawing;
using System.Drawing.Imaging;

namespace SVGoblin
{
    /// <summary>
    /// Converts raster images to SVG vector graphics. The pipeline quantizes the
    /// image into flat color layers, traces each layer's region boundaries along
    /// the cracks between pixels, smooths and simplifies the resulting polygons
    /// and converts them to line and cubic Bezier segments.
    /// </summary>
    public class Vectorizer
    {
        private readonly VectorizerOptions _options;

        // Rebuilt per run so tolerance changes between runs are picked up.
        private CurveFitOptions _fitOptions = new();

        /// <summary>Statistics from the most recent vectorization, if any.</summary>
        public VectorizeStats? LastStats { get; private set; }

        public Vectorizer(VectorizerOptions? options = null)
        {
            _options = options ?? new VectorizerOptions();
        }

        /// <summary>
        /// Vectorizes an image file to an SVG file.
        /// </summary>
        /// <param name="inputPath">Path to the input image (PNG).</param>
        /// <param name="outputPath">Path to the output SVG file.</param>
        public void VectorizeToDisk(string inputPath, string outputPath)
        {
            using var bitmap = new Bitmap(inputPath);
            var svg = Vectorize(bitmap);
            File.WriteAllText(outputPath, svg);
        }

        /// <summary>
        /// Vectorizes a bitmap to SVG markup.
        /// </summary>
        public string Vectorize(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            uint[] pixels = ReadPixels(bitmap, width, height);
            _fitOptions = BuildFitOptions();

            var traced = new List<TracedLayer>();
            foreach (var layer in BuildColorLayers(pixels, width, height))
            {
                var tracedLayer = TraceLayer(layer, width, height);
                if (tracedLayer != null)
                    traced.Add(tracedLayer);
            }

            LastStats = new VectorizeStats
            {
                Layers = traced.Count,
                Loops = traced.Sum(layer => layer.Loops.Count),
                Segments = traced.Sum(layer => layer.Loops.Sum(loop => loop.Count)),
            };

            return SvgWriter.Write(traced, width, height, _options);
        }

        private CurveFitOptions BuildFitOptions() => new()
        {
            CurveTolerance = _options.CurveTolerance,

            // Straight stretches must hug their chord clearly tighter than the
            // simplification tolerance, otherwise long chords across gentle
            // curves would be mistaken for genuine straight lines.
            LineTolerance = 0.5 * _options.SimplifyTolerance,

            CornerAngleThreshold = _options.CornerAngleThreshold,
        };

        private List<ColorLayer> BuildColorLayers(uint[] pixels, int width, int height)
        {
            var layers = _options.Mode == VectorizeMode.BlackWhite
                ? BinarizeSingleLayer(pixels)
                : ColorQuantizer.Quantize(pixels, width, height, _options);

            // Larger layers first so smaller shapes draw on top.
            layers.Sort((a, b) => b.PixelCount.CompareTo(a.PixelCount));
            return layers;
        }

        /// <summary>
        /// Traces one color layer's region boundaries into segment loops.
        /// Returns null when the layer has no shapes worth keeping.
        /// </summary>
        private TracedLayer? TraceLayer(ColorLayer layer, int width, int height)
        {
            var loops = new List<List<PathSegment>>();
            foreach (var contour in ContourTracer.Trace(layer.Mask, width, height))
            {
                var loop = BuildLoop(contour);
                if (loop != null)
                    loops.Add(loop);
            }

            return loops.Count > 0 ? new TracedLayer(layer.Color, loops) : null;
        }

        /// <summary>
        /// Smooths, simplifies and segments one traced contour. Returns null
        /// for contours too small or too degenerate to keep.
        /// </summary>
        private List<PathSegment>? BuildLoop(List<Point> contour)
        {
            if (Math.Abs(ContourTracer.SignedArea(contour)) < _options.MinShapeArea)
                return null;

            var smoothed = PathSimplifier.SmoothClosed(contour, _options.ContourSmoothPasses);
            var polygon = PathSimplifier.SimplifyClosed(smoothed, _options.SimplifyTolerance, out var sourceIndex);
            if (polygon.Count < 3)
                return null;

            return BuildSegments(new SimplifiedContour(polygon, smoothed, sourceIndex));
        }

        private List<PathSegment> BuildSegments(SimplifiedContour shape)
        {
            if (!_options.EnableCurveFitting)
                return CurveFitter.LinesFromPolygon(shape.Polygon);

            return _options.CurveMode == CurveMode.CatmullRom
                ? CurveFitter.FitClosedCatmullRom(shape, _fitOptions)
                : CurveFitter.FitClosed(shape, _fitOptions);
        }

        private List<ColorLayer> BinarizeSingleLayer(uint[] pixels)
        {
            var layer = new ColorLayer(Color.Black, pixels.Length);
            for (int i = 0; i < pixels.Length; i++)
            {
                if (!IsForeground(pixels[i]))
                    continue;

                layer.Mask[i] = 1;
                layer.PixelCount++;
            }

            return layer.PixelCount > 0 ? new List<ColorLayer> { layer } : new List<ColorLayer>();
        }

        /// <summary>Opaque pixel darker than the black/white threshold.</summary>
        private bool IsForeground(uint pixel)
        {
            if (!Rgba.IsOpaque(pixel))
                return false;

            int grayscale = (int)(0.299 * Rgba.Red(pixel) + 0.587 * Rgba.Green(pixel) + 0.114 * Rgba.Blue(pixel));
            return grayscale < _options.Threshold;
        }

        private static uint[] ReadPixels(Bitmap bitmap, int width, int height)
        {
            var pixels = new uint[width * height];
            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    byte* basePtr = (byte*)data.Scan0;
                    for (int y = 0; y < height; y++)
                    {
                        uint* row = (uint*)(basePtr + (long)y * data.Stride);
                        for (int x = 0; x < width; x++)
                            pixels[y * width + x] = row[x];
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return pixels;
        }
    }

    /// <summary>A traced color layer ready for SVG serialization.</summary>
    internal sealed class TracedLayer
    {
        public Color Color { get; }
        public List<List<PathSegment>> Loops { get; }

        public TracedLayer(Color color, List<List<PathSegment>> loops)
        {
            Color = color;
            Loops = loops;
        }
    }

    /// <summary>Summary of the most recent vectorization.</summary>
    public sealed class VectorizeStats
    {
        /// <summary>Number of color layers in the output.</summary>
        public int Layers { get; init; }

        /// <summary>Number of closed boundary loops across all layers.</summary>
        public int Loops { get; init; }

        /// <summary>Number of line/curve segments across all loops.</summary>
        public int Segments { get; init; }
    }

    public enum VectorizeMode
    {
        /// <summary>Quantize the image into flat color layers and trace each one.</summary>
        Color,

        /// <summary>Threshold the image to a single black layer.</summary>
        BlackWhite,
    }

    public enum CurveMode
    {
        /// <summary>Least-squares cubic Bezier fitting (Schneider's algorithm).</summary>
        Schneider,

        /// <summary>Centripetal Catmull-Rom interpolation through the simplified vertices.</summary>
        CatmullRom,
    }

    public class VectorizerOptions
    {
        /// <summary>Color or black/white tracing.</summary>
        public VectorizeMode Mode { get; set; } = VectorizeMode.Color;

        /// <summary>Maximum number of flat colors in Color mode.</summary>
        public int MaxColors { get; set; } = 8;

        /// <summary>
        /// RGB distance within which two colors are considered the same flat
        /// color (also used to separate foreground from the background color).
        /// </summary>
        public int ColorTolerance { get; set; } = 48;

        /// <summary>Grayscale cutoff (0-255) for BlackWhite mode.</summary>
        public int Threshold { get; set; } = 128;

        /// <summary>
        /// Maximum deviation, in pixels, allowed when simplifying traced
        /// boundaries (Ramer-Douglas-Peucker).
        /// </summary>
        public double SimplifyTolerance { get; set; } = 1.0;

        /// <summary>
        /// Number of [1 2 1]/4 smoothing passes applied to traced contours
        /// before simplification; removes pixel-staircase and quantization
        /// noise at a cost of ~0.35 px corner rounding per pass. 0 disables.
        /// </summary>
        public int ContourSmoothPasses { get; set; } = 2;

        /// <summary>Whether to fit cubic Bezier curves; otherwise emit line segments.</summary>
        public bool EnableCurveFitting { get; set; } = true;

        /// <summary>Curve engine used when <see cref="EnableCurveFitting"/> is true.</summary>
        public CurveMode CurveMode { get; set; } = CurveMode.Schneider;

        /// <summary>Maximum curve-fitting error, in pixels.</summary>
        public double CurveTolerance { get; set; } = 1.5;

        /// <summary>
        /// Minimum turn angle, in degrees, for a vertex to be kept as a sharp
        /// corner during curve fitting.
        /// </summary>
        public double CornerAngleThreshold { get; set; } = 60;

        /// <summary>Minimum area, in square pixels, for a shape to be kept.</summary>
        public int MinShapeArea { get; set; } = 4;

        /// <summary>
        /// Optional background fill (any SVG color string). Null leaves the
        /// background transparent.
        /// </summary>
        public string? BackgroundColor { get; set; }
    }
}
