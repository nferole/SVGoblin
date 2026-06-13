using System.Drawing;

namespace SVGoblin.Tests.Support
{
    /// <summary>
    /// Geometry helpers for divergence assertions: Bezier sampling, deviation
    /// from a source polygon, finiteness and boundedness checks.
    /// </summary>
    internal static class GeometryAsserts
    {
        /// <summary>Evaluates a segment at t in [0,1]: cubic Bezier for curves, lerp for lines.</summary>
        public static PointF Eval(PathSegment s, double t)
        {
            if (!s.IsCurve)
            {
                return new PointF(
                    (float)(s.Start.X + (s.End.X - s.Start.X) * t),
                    (float)(s.Start.Y + (s.End.Y - s.Start.Y) * t));
            }

            double mt = 1 - t;
            double b0 = mt * mt * mt;
            double b1 = 3 * t * mt * mt;
            double b2 = 3 * t * t * mt;
            double b3 = t * t * t;
            return new PointF(
                (float)(b0 * s.Start.X + b1 * s.Control1.X + b2 * s.Control2.X + b3 * s.End.X),
                (float)(b0 * s.Start.Y + b1 * s.Control1.Y + b2 * s.Control2.Y + b3 * s.End.Y));
        }

        /// <summary>Samples every segment at <paramref name="samplesPerSegment"/> points (t in [0,1)).</summary>
        public static List<PointF> Sample(IReadOnlyList<PathSegment> segments, int samplesPerSegment = 32)
        {
            var points = new List<PointF>(segments.Count * samplesPerSegment);
            foreach (var s in segments)
            {
                for (int i = 0; i < samplesPerSegment; i++)
                    points.Add(Eval(s, (double)i / samplesPerSegment));
            }

            return points;
        }

        /// <summary>Minimum distance from a point to a closed polyline.</summary>
        public static double DistanceToClosedPolyline(PointF p, IReadOnlyList<PointF> poly)
        {
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                double d = SegmentDistSq(p, poly[i], poly[(i + 1) % poly.Count]);
                if (d < best)
                    best = d;
            }

            return Math.Sqrt(best);
        }

        /// <summary>
        /// Max distance from any sampled curve point to the source polygon
        /// (curve wandering away from its input).
        /// </summary>
        public static double MaxDeviation(IReadOnlyList<PathSegment> segments, IReadOnlyList<PointF> sourcePolygon, int samplesPerSegment = 32)
        {
            double max = 0;
            foreach (var p in Sample(segments, samplesPerSegment))
                max = Math.Max(max, DistanceToClosedPolyline(p, sourcePolygon));
            return max;
        }

        /// <summary>
        /// Symmetric deviation: curve samples vs the polygon AND every polygon
        /// vertex vs the sampled curve (catches under-fitting too).
        /// </summary>
        public static double SymmetricMaxDeviation(IReadOnlyList<PathSegment> segments, IReadOnlyList<PointF> sourcePolygon, int samplesPerSegment = 32)
        {
            var sampled = Sample(segments, samplesPerSegment);
            double max = 0;
            foreach (var p in sampled)
                max = Math.Max(max, DistanceToClosedPolyline(p, sourcePolygon));
            foreach (var v in sourcePolygon)
                max = Math.Max(max, DistanceToClosedPolyline(v, sampled));
            return max;
        }

        /// <summary>Max deviation between two sampled closed point sequences, both directions.</summary>
        public static double SymmetricMaxDeviation(IReadOnlyList<PointF> a, IReadOnlyList<PointF> b)
        {
            double max = 0;
            foreach (var p in a)
                max = Math.Max(max, DistanceToClosedPolyline(p, b));
            foreach (var p in b)
                max = Math.Max(max, DistanceToClosedPolyline(p, a));
            return max;
        }

        public static void AssertAllFinite(IReadOnlyList<PathSegment> segments)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                foreach (var (p, name) in SegmentPoints(s))
                {
                    Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y),
                        $"Segment {i} {name} is not finite: ({p.X}, {p.Y})");
                }
            }
        }

        public static void AssertWithinBounds(IReadOnlyList<PathSegment> segments, RectangleF bbox, float margin)
        {
            var inflated = RectangleF.Inflate(bbox, margin, margin);
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                foreach (var (p, name) in SegmentPoints(s))
                {
                    Assert.True(inflated.Contains(p),
                        $"Segment {i} {name} ({p.X}, {p.Y}) escapes bounds {inflated} (input bbox {bbox} + margin {margin})");
                }
            }
        }

        public static void AssertClosedChain(IReadOnlyList<PathSegment> segments, float eps)
        {
            Assert.True(segments.Count > 0, "Expected at least one segment");
            for (int i = 0; i < segments.Count; i++)
            {
                PointF end = segments[i].End;
                PointF next = segments[(i + 1) % segments.Count].Start;
                double gap = Distance(end, next);
                Assert.True(gap <= eps,
                    $"Chain gap of {gap} between segment {i} end ({end.X}, {end.Y}) and segment {(i + 1) % segments.Count} start ({next.X}, {next.Y})");
            }
        }

        /// <summary>
        /// Asserts every junction between a line segment and an adjacent curve
        /// is tangent-continuous: the curve's tangent at the joint may deviate
        /// from the line's direction by at most <paramref name="maxAngleDeg"/>.
        /// </summary>
        public static void AssertLineCurveJunctionsTangent(IReadOnlyList<PathSegment> segments, double maxAngleDeg)
        {
            double minDot = Math.Cos(maxAngleDeg * Math.PI / 180.0);
            for (int i = 0; i < segments.Count; i++)
            {
                PathSegment current = segments[i];
                PathSegment next = segments[(i + 1) % segments.Count];
                if (!current.IsCurve && next.IsCurve)
                    AssertJunctionAligned(Direction(current.Start, current.End), Direction(next.Start, next.Control1), minDot, i);
                if (current.IsCurve && !next.IsCurve)
                    AssertJunctionAligned(Direction(current.Control2, current.End), Direction(next.Start, next.End), minDot, i);
            }
        }

        private static void AssertJunctionAligned((double X, double Y) line, (double X, double Y) curve, double minDot, int index)
        {
            double dot = line.X * curve.X + line.Y * curve.Y;
            Assert.True(dot >= minDot,
                $"Kink at the line/curve junction after segment {index}: directions ({line.X}, {line.Y}) vs ({curve.X}, {curve.Y})");
        }

        private static (double X, double Y) Direction(PointF from, PointF to)
        {
            double dx = to.X - from.X, dy = to.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            return len > 1e-12 ? (dx / len, dy / len) : (0, 0);
        }

        public static double ShoelaceArea(IReadOnlyList<PointF> points)
        {
            double sum = 0;
            for (int i = 0; i < points.Count; i++)
            {
                PointF a = points[i];
                PointF b = points[(i + 1) % points.Count];
                sum += (double)a.X * b.Y - (double)b.X * a.Y;
            }

            return sum / 2.0;
        }

        public static RectangleF BoundingBox(IReadOnlyList<PointF> points)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in points)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public static double Distance(PointF a, PointF b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static IEnumerable<(PointF Point, string Name)> SegmentPoints(PathSegment s)
        {
            yield return (s.Start, "Start");
            yield return (s.Control1, "Control1");
            yield return (s.Control2, "Control2");
            yield return (s.End, "End");
        }

        private static double SegmentDistSq(PointF p, PointF a, PointF b)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double apx = p.X - a.X, apy = p.Y - a.Y;
            double lenSq = abx * abx + aby * aby;
            double t = lenSq <= 1e-12 ? 0 : Math.Clamp((apx * abx + apy * aby) / lenSq, 0, 1);
            double dx = apx - t * abx, dy = apy - t * aby;
            return dx * dx + dy * dy;
        }
    }
}
