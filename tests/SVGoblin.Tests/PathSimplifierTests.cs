using System.Drawing;
using SVGoblin.Tests.Support;

namespace SVGoblin.Tests
{
    /// <summary>
    /// Divergence tests for smoothing (must not move points outside the input,
    /// shift the shape or shrink it unboundedly) and RDP simplification (must
    /// honor its distance guarantee and index mapping).
    /// </summary>
    public class PathSimplifierTests
    {
        [Fact]
        public void SmoothClosed_ZeroPasses_ReturnsInputUnchanged()
        {
            var contour = PolygonFactory.DenseSquareLattice(8);
            var smoothed = PathSimplifier.SmoothClosed(contour, 0);

            Assert.Equal(contour.Count, smoothed.Count);
            for (int i = 0; i < contour.Count; i++)
                Assert.Equal(new PointF(contour[i].X, contour[i].Y), smoothed[i]);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        public void SmoothClosed_ConvexCombination_StaysInsideInputBBox(int passes)
        {
            var contour = PolygonFactory.DenseSquareLattice(20);
            var smoothed = PathSimplifier.SmoothClosed(contour, passes);

            // [1 2 1]/4 is a convex combination: no smoothed point may escape
            // the input bounding box (inclusive edges; RectangleF.Contains
            // excludes right/bottom) beyond float rounding.
            const float eps = 1e-4f;
            var bbox = GeometryAsserts.BoundingBox(contour.Select(p => new PointF(p.X, p.Y)).ToList());
            foreach (var p in smoothed)
            {
                Assert.True(
                    p.X >= bbox.Left - eps && p.X <= bbox.Right + eps &&
                    p.Y >= bbox.Top - eps && p.Y <= bbox.Bottom + eps,
                    $"Smoothing pushed ({p.X}, {p.Y}) outside the input bounds {bbox}");
            }
        }

        [Fact]
        public void SmoothClosed_PreservesCentroid()
        {
            // The cyclic [1 2 1]/4 filter is doubly stochastic, so the vertex
            // centroid must not drift - a sensitive detector of asymmetric
            // distortion.
            var contour = PolygonFactory.DenseSquareLattice(20, 5, 9);
            var smoothed = PathSimplifier.SmoothClosed(contour, 5);

            double cxBefore = contour.Average(p => (double)p.X);
            double cyBefore = contour.Average(p => (double)p.Y);
            double cxAfter = smoothed.Average(p => (double)p.X);
            double cyAfter = smoothed.Average(p => (double)p.Y);

            Assert.True(Math.Abs(cxBefore - cxAfter) < 1e-3 && Math.Abs(cyBefore - cyAfter) < 1e-3,
                $"Centroid drifted from ({cxBefore}, {cyBefore}) to ({cxAfter}, {cyAfter})");
        }

        [Fact]
        public void SmoothClosed_DenseSquare_10Passes_AreaShrinkBounded()
        {
            var contour = PolygonFactory.DenseSquareLattice(20);
            var smoothed = PathSimplifier.SmoothClosed(contour, 10);

            double before = Math.Abs(GeometryAsserts.ShoelaceArea(contour.Select(p => new PointF(p.X, p.Y)).ToList()));
            double after = Math.Abs(GeometryAsserts.ShoelaceArea(smoothed));

            // Smoothing only rounds the four corners (~0.35 px per pass), so
            // even ten passes must keep the bulk of the area.
            Assert.True(after >= 0.9 * before,
                $"Smoothing shrank the area from {before} to {after}");
        }

        [Fact]
        public void SmoothClosed_TwoPoints_EarlyReturnNoSmoothing()
        {
            var contour = new List<Point> { new(1, 2), new(7, 8) };
            var smoothed = PathSimplifier.SmoothClosed(contour, 5);

            Assert.Equal(new List<PointF> { new(1, 2), new(7, 8) }, smoothed);
        }

        [Fact]
        public void SimplifyClosed_RdpGuarantee_AllInputPointsWithinTolerance()
        {
            const double tolerance = 1.0;
            var pts = PolygonFactory.Circle(256, 20, 32, 32);
            var simplified = PathSimplifier.SimplifyClosed(pts, tolerance, out _);

            Assert.True(simplified.Count < pts.Count, "Expected the dense circle to be simplified");
            foreach (var p in pts)
            {
                double d = GeometryAsserts.DistanceToClosedPolyline(p, simplified);
                Assert.True(d <= tolerance + 1e-6,
                    $"Point ({p.X}, {p.Y}) is {d} px from the simplified polyline (tolerance {tolerance})");
            }
        }

        [Fact]
        public void SimplifyClosed_ToleranceZero_KeepsAllPoints()
        {
            var pts = PolygonFactory.Circle(10, 5);
            var simplified = PathSimplifier.SimplifyClosed(pts, 0, out var sourceIndex);

            Assert.Equal(pts, simplified);
            Assert.Equal(PolygonFactory.IdentityIndex(10), sourceIndex);
        }

        [Fact]
        public void SimplifyClosed_FourOrFewerPoints_KeepsAll()
        {
            var pts = PolygonFactory.Square(20);
            var simplified = PathSimplifier.SimplifyClosed(pts, 5.0, out var sourceIndex);

            Assert.Equal(pts, simplified);
            Assert.Equal(PolygonFactory.IdentityIndex(4), sourceIndex);
        }

        [Fact]
        public void SimplifyClosed_SourceIndexMapsBackExactly()
        {
            var pts = PolygonFactory.Circle(256, 20, 32, 32);
            var simplified = PathSimplifier.SimplifyClosed(pts, 1.0, out var sourceIndex);

            // The curve fitter trusts this mapping to look up contour stretches.
            Assert.Equal(simplified.Count, sourceIndex.Count);
            for (int i = 0; i < simplified.Count; i++)
                Assert.Equal(pts[sourceIndex[i]], simplified[i]);
        }

        [Fact]
        public void SimplifyClosed_AllPointsIdentical_NoCrash()
        {
            var pts = Enumerable.Repeat(new PointF(5, 5), 10).ToList();
            var simplified = PathSimplifier.SimplifyClosed(pts, 1.0, out var sourceIndex);

            Assert.True(simplified.Count >= 1);
            Assert.All(simplified, p => Assert.Equal(new PointF(5, 5), p));
            Assert.All(sourceIndex, i => Assert.InRange(i, 0, 9));
        }

        [Fact]
        public void SimplifyClosed_DuplicateRuns_NoCrash()
        {
            var pts = PolygonFactory.WithDuplicates(PolygonFactory.Circle(32, 20, 32, 32), 5, 4);
            var simplified = PathSimplifier.SimplifyClosed(pts, 1.0, out var sourceIndex);

            Assert.True(simplified.Count >= 1);
            Assert.Equal(simplified.Count, sourceIndex.Count);
            Assert.All(sourceIndex, i => Assert.InRange(i, 0, pts.Count - 1));
            for (int i = 0; i < simplified.Count; i++)
                Assert.Equal(pts[sourceIndex[i]], simplified[i]);
        }
    }
}
