using System.Drawing;

namespace SVGoblin.Tests.Support
{
    /// <summary>Synthetic polygons and direct-call wrappers for the curve fitter.</summary>
    internal static class PolygonFactory
    {
        /// <summary>Axis-aligned square, counter-clockwise in y-down coordinates.</summary>
        public static List<PointF> Square(float side, float offsetX = 0, float offsetY = 0) => new()
        {
            new PointF(offsetX, offsetY),
            new PointF(offsetX, offsetY + side),
            new PointF(offsetX + side, offsetY + side),
            new PointF(offsetX + side, offsetY),
        };

        /// <summary>Square outline as a dense lattice of unit-spaced points (collinear runs along each edge).</summary>
        public static List<Point> DenseSquareLattice(int side, int offsetX = 0, int offsetY = 0)
        {
            var pts = new List<Point>(4 * side);
            for (int i = 0; i < side; i++) pts.Add(new Point(offsetX + i, offsetY));
            for (int i = 0; i < side; i++) pts.Add(new Point(offsetX + side, offsetY + i));
            for (int i = 0; i < side; i++) pts.Add(new Point(offsetX + side - i, offsetY + side));
            for (int i = 0; i < side; i++) pts.Add(new Point(offsetX, offsetY + side - i));
            return pts;
        }

        /// <summary>Regular n-gon approximating a circle. Vertex k sits at angle 2*pi*k/n.</summary>
        public static List<PointF> Circle(int n, double radius, double centerX = 0, double centerY = 0)
        {
            var pts = new List<PointF>(n);
            for (int k = 0; k < n; k++)
            {
                double a = 2 * Math.PI * k / n;
                pts.Add(new PointF((float)(centerX + radius * Math.Cos(a)), (float)(centerY + radius * Math.Sin(a))));
            }

            return pts;
        }

        /// <summary>Copies the polygon and duplicates the vertex at <paramref name="atIndex"/> <paramref name="count"/> extra times.</summary>
        public static List<PointF> WithDuplicates(List<PointF> polygon, int atIndex, int count)
        {
            var pts = new List<PointF>(polygon);
            for (int i = 0; i < count; i++)
                pts.Insert(atIndex, polygon[atIndex]);
            return pts;
        }

        /// <summary>
        /// Inserts a near-duplicate of the vertex at <paramref name="atIndex"/>,
        /// offset by <paramref name="eps"/> in x. Only representable when the
        /// vertex's x coordinate is near zero (PointF is float), so callers
        /// should use polygons centered at the origin and an index whose x ~ 0.
        /// </summary>
        public static List<PointF> WithNearDuplicate(List<PointF> polygon, int atIndex, double eps)
        {
            var pts = new List<PointF>(polygon);
            var v = polygon[atIndex];
            pts.Insert(atIndex + 1, new PointF((float)(v.X + eps), v.Y));
            return pts;
        }

        /// <summary>
        /// Stadium outline: two horizontal sides of <paramref name="sideLength"/>
        /// joined by semicircular caps of <paramref name="radius"/>, with
        /// vertices spaced roughly 2 px apart so every cap turn angle stays
        /// well below the corner threshold.
        /// </summary>
        public static List<PointF> Stadium(double sideLength, double radius)
        {
            int pointsPerSide = (int)(sideLength / 2);
            int pointsPerCap = (int)(Math.PI * radius / 2);
            var pts = new List<PointF>();

            for (int i = 0; i < pointsPerSide; i++)
                pts.Add(new PointF((float)(sideLength * i / pointsPerSide), (float)-radius));
            for (int i = 0; i < pointsPerCap; i++)
                pts.Add(CapPoint(sideLength, radius, -Math.PI / 2 + Math.PI * i / pointsPerCap));
            for (int i = 0; i < pointsPerSide; i++)
                pts.Add(new PointF((float)(sideLength - sideLength * i / pointsPerSide), (float)radius));
            for (int i = 0; i < pointsPerCap; i++)
                pts.Add(CapPoint(0, radius, Math.PI / 2 + Math.PI * i / pointsPerCap));

            return pts;
        }

        private static PointF CapPoint(double centerX, double radius, double angle)
            => new((float)(centerX + radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));

        public static List<int> IdentityIndex(int n)
        {
            var index = new List<int>(n);
            for (int i = 0; i < n; i++)
                index.Add(i);
            return index;
        }

        /// <summary>
        /// Calls Schneider fitting with the polygon as its own contour.
        /// lineTolerance 0 disables straight-run detection, isolating the fit.
        /// </summary>
        public static List<PathSegment> FitDirect(List<PointF> polygon, double tolerance = 1.5, double lineTolerance = 0, double cornerAngleDeg = 60)
            => CurveFitter.FitClosed(SelfContour(polygon), FitOptions(tolerance, lineTolerance, cornerAngleDeg));

        /// <summary>Like <see cref="FitDirect"/> but for the Catmull-Rom mode.</summary>
        public static List<PathSegment> FitCatmullRomDirect(List<PointF> polygon, double tolerance = 1.5, double lineTolerance = 0, double cornerAngleDeg = 60)
            => CurveFitter.FitClosedCatmullRom(SelfContour(polygon), FitOptions(tolerance, lineTolerance, cornerAngleDeg));

        private static SimplifiedContour SelfContour(List<PointF> polygon)
            => new(polygon, polygon, IdentityIndex(polygon.Count));

        private static CurveFitOptions FitOptions(double tolerance, double lineTolerance, double cornerAngleDeg) => new()
        {
            CurveTolerance = tolerance,
            LineTolerance = lineTolerance,
            CornerAngleThreshold = cornerAngleDeg,
        };
    }
}
