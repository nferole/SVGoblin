using System.Drawing;
using SVGoblin.Tests.Support;

namespace SVGoblin.Tests
{
    /// <summary>
    /// Divergence tests for the curve fitter: outputs must stay finite, stay
    /// near the input geometry and terminate, even for degenerate polygons.
    /// </summary>
    public class CurveFitterTests
    {
        // --- Schneider fitting ----------------------------------------------

        [Fact]
        public void FitClosed_Circle64_AllOutputsFinite()
        {
            var circle = PolygonFactory.Circle(64, 20, 32, 32);
            var segments = PolygonFactory.FitDirect(circle);

            Assert.NotEmpty(segments);
            GeometryAsserts.AssertAllFinite(segments);
        }

        [Fact]
        public void FitClosed_Circle_MaxDeviationWithinToleranceTimes3()
        {
            const double tolerance = 1.5;
            var circle = PolygonFactory.Circle(64, 20, 32, 32);
            var segments = PolygonFactory.FitDirect(circle, tolerance);

            // ComputeMaxError only samples at the original parameter values and
            // the two-point base case has no error check at all, so allow 3x.
            double deviation = GeometryAsserts.MaxDeviation(segments, circle);
            Assert.True(deviation <= 3 * tolerance,
                $"Fitted curve diverges {deviation} px from the source polygon (tolerance {tolerance})");
        }

        [Fact]
        public void FitClosed_Square_CornersSurviveAsSegmentEndpoints()
        {
            var square = PolygonFactory.Square(20);
            var segments = PolygonFactory.FitDirect(square, lineTolerance: 0.5);

            Assert.Equal(4, segments.Count);
            Assert.All(segments, s => Assert.False(s.IsCurve));
            foreach (var corner in square)
            {
                Assert.Contains(segments, s =>
                    GeometryAsserts.Distance(s.Start, corner) < 1e-3 ||
                    GeometryAsserts.Distance(s.End, corner) < 1e-3);
            }
        }

        [Fact]
        public void FitClosed_CollinearOutAndBackPolygon_NoThrowAllFinite()
        {
            // Zero-area "polygon" going out along a line and back: 180-degree
            // turns produce opposed tangents and a singular least-squares
            // matrix, and the loop chord has length zero.
            var outAndBack = new List<PointF>
            {
                new(0, 0), new(10, 0), new(20, 0), new(30, 0), new(20, 0), new(10, 0),
            };

            var segments = PolygonFactory.FitDirect(outAndBack, cornerAngleDeg: 181);

            Assert.NotEmpty(segments);
            GeometryAsserts.AssertAllFinite(segments);
        }

        [Fact]
        public void FitClosed_DuplicateConsecutivePoints_AllFiniteWithinBBox()
        {
            var circle = PolygonFactory.Circle(32, 20);
            var polygon = PolygonFactory.WithDuplicates(circle, 8, 2);

            var segments = PolygonFactory.FitDirect(polygon);

            GeometryAsserts.AssertAllFinite(segments);
            GeometryAsserts.AssertWithinBounds(segments, GeometryAsserts.BoundingBox(polygon), 20);
        }

        [Theory]
        [InlineData(1e-3)]
        [InlineData(1e-6)]
        [InlineData(1e-9)]
        [InlineData(1e-12)] // collapses to an exact duplicate in float, the fully degenerate case
        public void FitClosed_NearZeroChords_AllFiniteAndBounded(double eps)
        {
            // Vertex 8 of a 32-gon at angle pi/2 sits at x ~ 0, where a float
            // can still represent tiny x offsets.
            var circle = PolygonFactory.Circle(32, 20);
            var polygon = PolygonFactory.WithNearDuplicate(circle, 8, eps);

            var segments = PolygonFactory.FitDirect(polygon);

            GeometryAsserts.AssertAllFinite(segments);
            GeometryAsserts.AssertWithinBounds(segments, GeometryAsserts.BoundingBox(polygon), 20);
        }

        [Fact]
        public void FitClosed_ToleranceZero_TerminatesFinite()
        {
            // An unsatisfiable tolerance must bottom out at two-point chains
            // (one segment per adjacent vertex pair) instead of recursing forever.
            var circle = PolygonFactory.Circle(64, 20, 32, 32);
            var segments = PolygonFactory.FitDirect(circle, tolerance: 0);

            Assert.Equal(64, segments.Count);
            GeometryAsserts.AssertAllFinite(segments);
        }

        [Fact]
        public void FitClosed_HugeTolerance_ControlPointsBounded()
        {
            var circle = PolygonFactory.Circle(64, 20, 32, 32);
            var segments = PolygonFactory.FitDirect(circle, tolerance: 1e9);

            Assert.NotEmpty(segments);
            GeometryAsserts.AssertAllFinite(segments);

            // alphaMax = 3 x chord length is the only bound on the fit.
            var bbox = GeometryAsserts.BoundingBox(circle);
            float diagonal = (float)Math.Sqrt(bbox.Width * bbox.Width + bbox.Height * bbox.Height);
            GeometryAsserts.AssertWithinBounds(segments, bbox, 3 * diagonal);
        }

        [Fact]
        public void FitClosed_ExtremeCoordinates1e6_FiniteAndClosed()
        {
            var circle = PolygonFactory.Circle(64, 20, 1e6, 1e6);
            var segments = PolygonFactory.FitDirect(circle);

            GeometryAsserts.AssertAllFinite(segments);
            GeometryAsserts.AssertClosedChain(segments, 0.5f); // float ulp at 1e6 is 0.0625
        }

        [Fact]
        public void FitClosed_FullySmoothLoop_ClosesAtSplit()
        {
            // No corners and no straight runs: the loop is cut at one vertex
            // and fitted as a single chain that must close on itself.
            var circle = PolygonFactory.Circle(64, 20, 32, 32);
            var segments = PolygonFactory.FitDirect(circle, cornerAngleDeg: 180);

            GeometryAsserts.AssertClosedChain(segments, 1e-3f);
        }

        [Fact]
        public void FitClosed_Stadium_StraightSidesPinnedAsTangentContinuousLines()
        {
            // A stadium has no sharp corners, so its long flat sides are only
            // found by the straight-run scan (line tolerance > 0): each side
            // must be pinned as one line segment at least MinLineLength long,
            // and the cap curves must take over in the line's direction
            // instead of kinking at the non-corner junctions.
            var stadium = PolygonFactory.Stadium(40, 10);
            var segments = PolygonFactory.FitDirect(stadium, lineTolerance: 0.5);

            GeometryAsserts.AssertAllFinite(segments);
            GeometryAsserts.AssertClosedChain(segments, 1e-3f);

            var lines = segments.Where(s => !s.IsCurve).ToList();
            Assert.Equal(2, lines.Count);
            Assert.All(lines, s => Assert.True(GeometryAsserts.Distance(s.Start, s.End) >= 10,
                $"Pinned line from ({s.Start}) to ({s.End}) is shorter than MinLineLength"));

            GeometryAsserts.AssertLineCurveJunctionsTangent(segments, maxAngleDeg: 2);
        }

        [Fact]
        public void FitClosed_SameInputTwice_BitwiseIdenticalSegments()
        {
            var circle = PolygonFactory.Circle(48, 17.3, 25, 25);
            var first = PolygonFactory.FitDirect(circle);
            var second = PolygonFactory.FitDirect(circle);

            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Start, second[i].Start);
                Assert.Equal(first[i].Control1, second[i].Control1);
                Assert.Equal(first[i].Control2, second[i].Control2);
                Assert.Equal(first[i].End, second[i].End);
                Assert.Equal(first[i].IsCurve, second[i].IsCurve);
            }
        }

        // --- Catmull-Rom ------------------------------------------------------

        [Theory]
        [InlineData(1e-7)]
        [InlineData(1e-9)]
        [InlineData(1e-12)]
        public void FitClosedCatmullRom_NearDuplicatePoints_ControlPointsStayWithin2xBoundingBox(double eps)
        {
            // Tiny spans next to normal-length edges stress the hb/ha knot
            // ratios in KnotTangent against the 1e-6 span floor.
            var circle = PolygonFactory.Circle(32, 20);
            var polygon = PolygonFactory.WithNearDuplicate(circle, 8, eps);

            var segments = PolygonFactory.FitCatmullRomDirect(polygon);

            GeometryAsserts.AssertAllFinite(segments);
            GeometryAsserts.AssertWithinBounds(segments, GeometryAsserts.BoundingBox(polygon), 20);
        }

        [Fact]
        public void FitClosedCatmullRom_Circle_DeviationBoundedByEdgeLength()
        {
            var circle = PolygonFactory.Circle(64, 20, 32, 32);
            var segments = PolygonFactory.FitCatmullRomDirect(circle);

            // The spline interpolates every vertex, so it can only bulge
            // between knots - far less than half an edge length.
            double maxEdge = 2 * 20 * Math.Sin(Math.PI / 64);
            double deviation = GeometryAsserts.MaxDeviation(segments, circle);
            Assert.True(deviation <= maxEdge / 2,
                $"Catmull-Rom diverges {deviation} px from its knots (edge length {maxEdge})");
        }

        [Fact]
        public void FitClosedCatmullRom_NoCorners_ClosedChainContiguous()
        {
            var circle = PolygonFactory.Circle(64, 20, 32, 32);
            var segments = PolygonFactory.FitCatmullRomDirect(circle, cornerAngleDeg: 180);

            Assert.Equal(64, segments.Count);
            GeometryAsserts.AssertAllFinite(segments);
            GeometryAsserts.AssertClosedChain(segments, 1e-6f);
        }

        [Fact]
        public void LinesFromPolygon_RoundTripsVerticesExactly()
        {
            var square = PolygonFactory.Square(20, 3, 7);
            var segments = CurveFitter.LinesFromPolygon(square);

            Assert.Equal(4, segments.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.False(segments[i].IsCurve);
                Assert.Equal(square[i], segments[i].Start);
                Assert.Equal(square[(i + 1) % 4], segments[i].End);
            }
        }
    }
}
