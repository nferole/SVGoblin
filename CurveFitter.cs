using System.Drawing;

namespace SVGoblin
{
    /// <summary>A single SVG path segment: a straight line or a cubic Bezier.</summary>
    internal readonly struct PathSegment
    {
        public readonly PointF Start;
        public readonly PointF Control1;
        public readonly PointF Control2;
        public readonly PointF End;
        public readonly bool IsCurve;

        private PathSegment(PointF start, PointF control1, PointF control2, PointF end, bool isCurve)
        {
            Start = start;
            Control1 = control1;
            Control2 = control2;
            End = end;
            IsCurve = isCurve;
        }

        public static PathSegment Line(PointF a, PointF b) => new(a, a, b, b, false);

        public static PathSegment Curve(PointF p0, PointF c1, PointF c2, PointF p3) => new(p0, c1, c2, p3, true);
    }

    /// <summary>Tolerances for converting simplified polygons to segments.</summary>
    internal sealed class CurveFitOptions
    {
        /// <summary>Maximum curve-fitting error, in pixels.</summary>
        public double CurveTolerance { get; init; } = 1.5;

        /// <summary>
        /// Maximum deviation, in pixels, for a contour stretch to count as a
        /// straight line. Zero disables straight-run detection.
        /// </summary>
        public double LineTolerance { get; init; }

        /// <summary>
        /// Minimum turn angle, in degrees, for a vertex to be kept as a sharp
        /// corner.
        /// </summary>
        public double CornerAngleThreshold { get; init; } = 60;
    }

    /// <summary>
    /// A simplified closed polygon together with the smoothed contour it was
    /// derived from. <see cref="ContourIndex"/> maps each polygon vertex back
    /// to its index in <see cref="Contour"/>; straightness is tested against
    /// the contour because simplified vertices alone cannot distinguish a
    /// genuine straight stroke from a long chord across a gentle curve.
    /// </summary>
    internal sealed class SimplifiedContour
    {
        public List<PointF> Polygon { get; }
        public List<PointF> Contour { get; }
        public List<int> ContourIndex { get; }

        public SimplifiedContour(List<PointF> polygon, List<PointF> contour, List<int> contourIndex)
        {
            Polygon = polygon;
            Contour = contour;
            ContourIndex = contourIndex;
        }
    }

    /// <summary>
    /// Turns simplified closed polygons into line and cubic Bezier segments.
    /// Vertices with sharp turn angles are kept as corners and stretches whose
    /// underlying contour is straight are pinned as true line segments; the
    /// smooth stretches in between are fitted with Schneider's algorithm
    /// (Graphics Gems, "An Algorithm for Automatically Fitting Digitized
    /// Curves") or, alternatively, interpolated with a centripetal Catmull-Rom
    /// spline. Each instance performs a single fit.
    /// </summary>
    internal sealed class CurveFitter
    {
        /// <summary>Minimum chord length, in pixels, for a straight vertex run to be pinned as a line segment.</summary>
        private const double MinLineLength = 10.0;

        /// <summary>Maximum control-point deviation from the chord, in pixels, for a fitted curve to collapse to a line.</summary>
        private const double LineSnapTolerance = 0.5;

        private readonly List<PointF> _polygon;
        private readonly List<PointF> _contour;
        private readonly List<int> _contourIndex;
        private readonly CurveFitOptions _options;
        private readonly double _curveToleranceSq;
        private readonly bool _catmullRom;
        private readonly List<PathSegment> _result = new();

        // Built once at the start of Fit, then read by the per-span helpers.
        private bool[] _isCorner = Array.Empty<bool>();
        private Dictionary<int, int> _lineRuns = new();
        private Dictionary<int, int> _runStartByEnd = new();

        /// <summary>Converts a simplified closed polygon to line and curve segments.</summary>
        public static List<PathSegment> FitClosed(SimplifiedContour shape, CurveFitOptions options)
            => new CurveFitter(shape, options, catmullRom: false).Fit();

        /// <summary>
        /// Like <see cref="FitClosed"/> but interpolates the smooth stretches
        /// with a centripetal Catmull-Rom spline (one cubic per polygon edge)
        /// instead of least-squares fitting.
        /// </summary>
        public static List<PathSegment> FitClosedCatmullRom(SimplifiedContour shape, CurveFitOptions options)
            => new CurveFitter(shape, options, catmullRom: true).Fit();

        /// <summary>Emits a closed polygon as plain line segments.</summary>
        public static List<PathSegment> LinesFromPolygon(List<PointF> polygon)
        {
            var segments = new List<PathSegment>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++)
                segments.Add(PathSegment.Line(polygon[i], polygon[(i + 1) % polygon.Count]));
            return segments;
        }

        private CurveFitter(SimplifiedContour shape, CurveFitOptions options, bool catmullRom)
        {
            _polygon = shape.Polygon;
            _contour = shape.Contour;
            _contourIndex = shape.ContourIndex;
            _options = options;
            _curveToleranceSq = options.CurveTolerance * options.CurveTolerance;
            _catmullRom = catmullRom;
        }

        private List<PathSegment> Fit()
        {
            double[] turn = TurnAngles();
            _isCorner = MarkCorners(turn);
            _lineRuns = FindStraightRuns();
            _runStartByEnd = InvertRuns(_lineRuns);

            List<int> breaks = FindBreakpoints();
            if (breaks.Count == 0)
            {
                FitSmoothLoop(turn);
                return _result;
            }

            for (int j = 0; j < breaks.Count; j++)
                FitSpan(breaks[j], breaks[(j + 1) % breaks.Count]);

            return _result;
        }

        private double[] TurnAngles()
        {
            var turn = new double[_polygon.Count];
            for (int i = 0; i < turn.Length; i++)
                turn[i] = TurnAngle(i);
            return turn;
        }

        private bool[] MarkCorners(double[] turn)
        {
            var isCorner = new bool[turn.Length];
            for (int i = 0; i < turn.Length; i++)
                isCorner[i] = turn[i] >= _options.CornerAngleThreshold;
            return isCorner;
        }

        private static Dictionary<int, int> InvertRuns(Dictionary<int, int> runs)
        {
            var byEnd = new Dictionary<int, int>(runs.Count);
            foreach (var run in runs)
                byEnd[run.Value] = run.Key;
            return byEnd;
        }

        /// <summary>
        /// Segment boundaries: sharp corners plus the endpoints of straight
        /// runs (runs never cross a corner, so a run's endpoints are always
        /// adjacent in the result).
        /// </summary>
        private List<int> FindBreakpoints()
        {
            var isBreak = (bool[])_isCorner.Clone();
            foreach (var run in _lineRuns)
            {
                isBreak[run.Key] = true;
                isBreak[run.Value] = true;
            }

            var breaks = new List<int>();
            for (int i = 0; i < isBreak.Length; i++)
            {
                if (isBreak[i])
                    breaks.Add(i);
            }

            return breaks;
        }

        /// <summary>
        /// Fits a loop without corners or straight runs. For Schneider fitting
        /// the loop is cut at the sharpest vertex and the same tangent is used
        /// on both ends so the closure stays smooth.
        /// </summary>
        private void FitSmoothLoop(double[] turn)
        {
            if (_catmullRom)
            {
                EmitCatmullRomClosed();
                return;
            }

            int n = _polygon.Count;
            int split = 0;
            for (int i = 1; i < n; i++)
            {
                if (turn[i] > turn[split])
                    split = i;
            }

            var loop = ExtractChain(split, split);
            var prev = _polygon[(split - 1 + n) % n];
            var next = _polygon[(split + 1) % n];
            var tangent = new Vec(next.X - prev.X, next.Y - prev.Y).Normalized();
            new SchneiderFitter(loop, _curveToleranceSq, _result).Fit(tangent, tangent * -1);
        }

        /// <summary>Converts the stretch between two break vertices to segments.</summary>
        private void FitSpan(int from, int to)
        {
            if (_lineRuns.TryGetValue(from, out int runEnd) && runEnd == to)
            {
                _result.Add(PathSegment.Line(_polygon[from], _polygon[to]));
                return;
            }

            var chain = ExtractChain(from, to);
            if (chain.Length == 2)
            {
                _result.Add(PathSegment.Line(ToPointF(chain[0]), ToPointF(chain[1])));
                return;
            }

            if (ContourIsStraight(from, to))
            {
                _result.Add(PathSegment.Line(ToPointF(chain[0]), ToPointF(chain[^1])));
                return;
            }

            Vec tHat1 = StartTangent(from, chain);
            Vec tHat2 = EndTangent(to, chain);
            if (_catmullRom)
                EmitCatmullRom(chain, tHat1, tHat2);
            else
                new SchneiderFitter(chain, _curveToleranceSq, _result).Fit(tHat1, tHat2);
        }

        /// <summary>
        /// Tangent heading into a stretch: one-sided by default, but where the
        /// stretch meets a straight run at a non-corner breakpoint the line's
        /// direction is continued so the junction stays tangent-continuous.
        /// </summary>
        private Vec StartTangent(int vertex, Vec[] chain)
        {
            if (!_isCorner[vertex] && _runStartByEnd.TryGetValue(vertex, out int lineFrom))
                return (ToVec(_polygon[vertex]) - ToVec(_polygon[lineFrom])).Normalized();

            return (chain[1] - chain[0]).Normalized();
        }

        /// <summary>Like <see cref="StartTangent"/> but heading back into the stretch from its last vertex.</summary>
        private Vec EndTangent(int vertex, Vec[] chain)
        {
            if (!_isCorner[vertex] && _lineRuns.TryGetValue(vertex, out int lineTo))
                return (ToVec(_polygon[vertex]) - ToVec(_polygon[lineTo])).Normalized();

            return (chain[^2] - chain[^1]).Normalized();
        }

        /// <summary>
        /// Finds maximal vertex runs whose underlying contour stays within the
        /// line tolerance of the run's chord and whose chord is at least
        /// <see cref="MinLineLength"/> long. Runs may end at a corner but never
        /// pass through one, and do not overlap. Returns a map from run start
        /// to run end (polygon indices).
        /// </summary>
        private Dictionary<int, int> FindStraightRuns()
        {
            var runs = new Dictionary<int, int>();
            int n = _polygon.Count;
            if (_options.LineTolerance <= 0 || n < 2)
                return runs;

            // Scan from a corner when one exists so runs are not cut at an
            // arbitrary loop origin.
            int origin = Math.Max(0, Array.IndexOf(_isCorner, true));

            int i = 0;
            while (i < n)
            {
                int start = (origin + i) % n;
                int bestLength = 0;

                for (int length = 1; length < n && i + length <= n; length++)
                {
                    if (length > 1 && _isCorner[(start + length - 1) % n])
                        break;

                    int end = (start + length) % n;
                    if (!ContourIsStraight(start, end))
                        break;

                    if (ChordLength(start, end) >= MinLineLength)
                        bestLength = length;
                }

                if (bestLength > 0)
                {
                    runs[start] = (start + bestLength) % n;
                    i += bestLength;
                }
                else
                {
                    i++;
                }
            }

            return runs;
        }

        /// <summary>
        /// Whether every contour point strictly between two polygon vertices
        /// (wrapping) lies within the line tolerance of the vertices' chord.
        /// </summary>
        private bool ContourIsStraight(int fromVertex, int toVertex)
        {
            Vec chordA = ToVec(_polygon[fromVertex]);
            Vec chordB = ToVec(_polygon[toVertex]);
            double tolSq = _options.LineTolerance * _options.LineTolerance;

            int m = _contour.Count;
            int from = _contourIndex[fromVertex];
            int count = (_contourIndex[toVertex] - from + m) % m;
            if (count == 0)
                count = m;

            for (int i = 1; i < count; i++)
            {
                Vec p = ToVec(_contour[(from + i) % m]);
                if (SegmentDistSq(p, chordA, chordB) > tolSq)
                    return false;
            }

            return true;
        }

        private double ChordLength(int fromVertex, int toVertex)
            => (ToVec(_polygon[toVertex]) - ToVec(_polygon[fromVertex])).Length;

        private static double SegmentDistSq(Vec p, Vec a, Vec b)
        {
            Vec ab = b - a;
            Vec ap = p - a;
            double lenSq = ab.LengthSq;
            double t = lenSq <= 1e-12 ? 0 : Math.Clamp(ap.Dot(ab) / lenSq, 0, 1);
            Vec d = ap - ab * t;
            return d.LengthSq;
        }

        private double TurnAngle(int i)
        {
            int n = _polygon.Count;
            PointF prev = _polygon[(i - 1 + n) % n];
            PointF cur = _polygon[i];
            PointF next = _polygon[(i + 1) % n];
            var u = new Vec(cur.X - prev.X, cur.Y - prev.Y).Normalized();
            var v = new Vec(next.X - cur.X, next.Y - cur.Y).Normalized();
            double dot = Math.Clamp(u.Dot(v), -1.0, 1.0);
            return Math.Acos(dot) * (180.0 / Math.PI);
        }

        /// <summary>
        /// Returns polygon vertices from index <paramref name="from"/> to
        /// <paramref name="to"/> inclusive, wrapping around; if they are equal,
        /// the whole loop (ending back at the start point) is returned.
        /// </summary>
        private Vec[] ExtractChain(int from, int to)
        {
            int n = _polygon.Count;
            int count = (to - from + n) % n;
            if (count == 0)
                count = n;

            var chain = new Vec[count + 1];
            for (int i = 0; i <= count; i++)
            {
                PointF p = _polygon[(from + i) % n];
                chain[i] = new Vec(p.X, p.Y);
            }

            return chain;
        }

        // --- Centripetal Catmull-Rom ----------------------------------------
        // Knot spacing is sqrt of the chord length (alpha = 0.5), which avoids
        // the cusps and self-intersections of the uniform parameterization.
        // Each span is emitted as the exactly equivalent cubic Bezier.

        /// <summary>
        /// Interpolates an open chain, one cubic per edge. End tangent
        /// directions are supplied by the caller (one-sided, or aligned with
        /// an adjacent line segment).
        /// </summary>
        private void EmitCatmullRom(Vec[] v, Vec tHat1, Vec tHat2)
        {
            int spans = v.Length - 1;
            var h = new double[spans];
            for (int i = 0; i < spans; i++)
                h[i] = Math.Max(Math.Sqrt((v[i + 1] - v[i]).Length), 1e-6);

            // Derivatives with respect to the knot parameter; the one-sided
            // end derivatives have magnitude equal to the adjacent knot span.
            var tangents = new Vec[v.Length];
            tangents[0] = tHat1 * h[0];
            tangents[spans] = tHat2 * -h[spans - 1];
            for (int j = 1; j < spans; j++)
                tangents[j] = KnotTangent(v[j - 1], v[j], v[j + 1], h[j - 1], h[j]);

            for (int i = 0; i < spans; i++)
            {
                double k = h[i] / 3.0;
                Emit(_result, v[i], v[i] + tangents[i] * k, v[i + 1] - tangents[i + 1] * k, v[i + 1]);
            }
        }

        /// <summary>Interpolates a fully smooth closed loop (no corners or straight runs).</summary>
        private void EmitCatmullRomClosed()
        {
            int n = _polygon.Count;
            var h = new double[n];
            for (int i = 0; i < n; i++)
                h[i] = Math.Max(Math.Sqrt((ToVec(_polygon[(i + 1) % n]) - ToVec(_polygon[i])).Length), 1e-6);

            var tangents = new Vec[n];
            for (int j = 0; j < n; j++)
            {
                tangents[j] = KnotTangent(
                    ToVec(_polygon[(j - 1 + n) % n]), ToVec(_polygon[j]), ToVec(_polygon[(j + 1) % n]),
                    h[(j - 1 + n) % n], h[j]);
            }

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                double k = h[i] / 3.0;
                Emit(_result, ToVec(_polygon[i]), ToVec(_polygon[i]) + tangents[i] * k, ToVec(_polygon[next]) - tangents[next] * k, ToVec(_polygon[next]));
            }
        }

        /// <summary>
        /// Spline derivative at the middle of three consecutive points, with
        /// centripetal knot spans <paramref name="ha"/> (incoming) and
        /// <paramref name="hb"/> (outgoing).
        /// </summary>
        private static Vec KnotTangent(Vec prev, Vec cur, Vec next, double ha, double hb)
            => ((cur - prev) * (hb / ha) + (next - cur) * (ha / hb)) * (1.0 / (ha + hb));

        private static void Emit(List<PathSegment> result, Vec p0, Vec c1, Vec c2, Vec p3)
        {
            result.Add(IsEffectivelyStraight(p0, c1, c2, p3)
                ? PathSegment.Line(ToPointF(p0), ToPointF(p3))
                : PathSegment.Curve(ToPointF(p0), ToPointF(c1), ToPointF(c2), ToPointF(p3)));
        }

        /// <summary>
        /// Whether both control points hug the chord closely enough that the
        /// curve is indistinguishable from a straight line.
        /// </summary>
        private static bool IsEffectivelyStraight(Vec p0, Vec c1, Vec c2, Vec p3)
        {
            Vec chord = p3 - p0;
            double lenSq = chord.LengthSq;
            return lenSq >= 1e-9 && HugsChord(p0, chord, lenSq, c1) && HugsChord(p0, chord, lenSq, c2);
        }

        private static bool HugsChord(Vec p0, Vec chord, double lenSq, Vec c)
        {
            Vec d = c - p0;
            double t = d.Dot(chord) / lenSq;
            if (t < -0.1 || t > 1.1)
                return false;

            double cross = chord.X * d.Y - chord.Y * d.X;
            return cross * cross <= LineSnapTolerance * LineSnapTolerance * lenSq;
        }

        private static PointF ToPointF(Vec v) => new((float)v.X, (float)v.Y);

        private static Vec ToVec(PointF p) => new(p.X, p.Y);

        /// <summary>
        /// Least-squares cubic Bezier fitting of one open chain, following
        /// Schneider's algorithm. Conventions follow the original: tHat1 is
        /// the unit tangent at the first point heading into the chain, tHat2
        /// the unit tangent at the last point heading back into the chain.
        /// </summary>
        private sealed class SchneiderFitter
        {
            private readonly Vec[] _points;
            private readonly double _toleranceSq;
            private readonly List<PathSegment> _result;

            public SchneiderFitter(Vec[] points, double toleranceSq, List<PathSegment> result)
            {
                _points = points;
                _toleranceSq = toleranceSq;
                _result = result;
            }

            public void Fit(Vec tHat1, Vec tHat2) => FitCubic(0, _points.Length - 1, tHat1, tHat2);

            private void FitCubic(int first, int last, Vec tHat1, Vec tHat2)
            {
                if (last - first + 1 == 2)
                {
                    double dist = (_points[last] - _points[first]).Length / 3.0;
                    Emit(_result, _points[first], _points[first] + tHat1 * dist, _points[last] + tHat2 * dist, _points[last]);
                    return;
                }

                var u = ChordLengthParameterize(first, last);
                var bez = GenerateBezier(first, last, u, tHat1, tHat2);
                double maxError = ComputeMaxError(first, last, bez, u, out int split);

                if (maxError < _toleranceSq)
                {
                    Emit(_result, bez[0], bez[1], bez[2], bez[3]);
                    return;
                }

                // If the fit is close, try improving the parameterization before splitting.
                if (maxError < _toleranceSq * 16)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        u = Reparameterize(first, last, u, bez);
                        bez = GenerateBezier(first, last, u, tHat1, tHat2);
                        maxError = ComputeMaxError(first, last, bez, u, out split);
                        if (maxError < _toleranceSq)
                        {
                            Emit(_result, bez[0], bez[1], bez[2], bez[3]);
                            return;
                        }
                    }
                }

                Vec tHatCenter = ComputeCenterTangent(split);
                FitCubic(first, split, tHat1, tHatCenter);
                FitCubic(split, last, tHatCenter * -1, tHat2);
            }

            private Vec[] GenerateBezier(int first, int last, double[] uPrime, Vec tHat1, Vec tHat2)
            {
                double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
                int nPts = last - first + 1;

                for (int i = 0; i < nPts; i++)
                {
                    double u = uPrime[i];
                    Vec a0 = tHat1 * B1(u);
                    Vec a1 = tHat2 * B2(u);
                    c00 += a0.Dot(a0);
                    c01 += a0.Dot(a1);
                    c11 += a1.Dot(a1);

                    Vec tmp = _points[first + i] - (_points[first] * (B0(u) + B1(u)) + _points[last] * (B2(u) + B3(u)));
                    x0 += a0.Dot(tmp);
                    x1 += a1.Dot(tmp);
                }

                double detC0C1 = c00 * c11 - c01 * c01;
                double alphaL = Math.Abs(detC0C1) < 1e-12 ? 0 : (x0 * c11 - x1 * c01) / detC0C1;
                double alphaR = Math.Abs(detC0C1) < 1e-12 ? 0 : (c00 * x1 - c01 * x0) / detC0C1;

                double segLength = (_points[last] - _points[first]).Length;
                double epsilon = 1.0e-6 * segLength;
                double alphaMax = 3.0 * segLength;

                var bez = new Vec[4];
                bez[0] = _points[first];
                bez[3] = _points[last];
                if (!IsUsableAlpha(alphaL, epsilon, alphaMax) || !IsUsableAlpha(alphaR, epsilon, alphaMax))
                {
                    // Degenerate or runaway least-squares solution (with nearly
                    // parallel tangents the alphas blow up and the control points
                    // spike far outside the shape); fall back to the Wu/Barsky
                    // heuristic of a third of the chord length.
                    double dist = segLength / 3.0;
                    bez[1] = bez[0] + tHat1 * dist;
                    bez[2] = bez[3] + tHat2 * dist;
                }
                else
                {
                    bez[1] = bez[0] + tHat1 * alphaL;
                    bez[2] = bez[3] + tHat2 * alphaR;
                }

                return bez;
            }

            /// <summary>A finite control-point distance that neither collapses nor runs away.</summary>
            private static bool IsUsableAlpha(double alpha, double epsilon, double alphaMax)
                => !double.IsNaN(alpha) && alpha >= epsilon && alpha <= alphaMax;

            private double[] ChordLengthParameterize(int first, int last)
            {
                var u = new double[last - first + 1];
                for (int i = first + 1; i <= last; i++)
                    u[i - first] = u[i - first - 1] + (_points[i] - _points[i - 1]).Length;

                double total = u[^1];
                if (total <= 0)
                {
                    for (int i = 1; i < u.Length; i++)
                        u[i] = (double)i / (u.Length - 1);
                    return u;
                }

                for (int i = 1; i < u.Length; i++)
                    u[i] /= total;
                return u;
            }

            private double[] Reparameterize(int first, int last, double[] u, Vec[] bez)
            {
                var uPrime = new double[u.Length];
                for (int i = first; i <= last; i++)
                    uPrime[i - first] = Math.Clamp(NewtonRaphsonRootFind(bez, _points[i], u[i - first]), 0.0, 1.0);
                return uPrime;
            }

            private static double NewtonRaphsonRootFind(Vec[] q, Vec p, double u)
            {
                Vec qu = BezierEval(3, q, u);

                var q1 = new Vec[3];
                for (int i = 0; i < 3; i++)
                    q1[i] = (q[i + 1] - q[i]) * 3;
                var q2 = new Vec[2];
                for (int i = 0; i < 2; i++)
                    q2[i] = (q1[i + 1] - q1[i]) * 2;

                Vec q1u = BezierEval(2, q1, u);
                Vec q2u = BezierEval(1, q2, u);

                double numerator = (qu - p).Dot(q1u);
                double denominator = q1u.Dot(q1u) + (qu - p).Dot(q2u);
                if (Math.Abs(denominator) < 1e-12)
                    return u;

                return u - numerator / denominator;
            }

            private double ComputeMaxError(int first, int last, Vec[] bez, double[] u, out int splitPoint)
            {
                splitPoint = (first + last) / 2;
                double maxDist = 0;
                for (int i = first + 1; i < last; i++)
                {
                    Vec p = BezierEval(3, bez, u[i - first]);
                    double dist = (p - _points[i]).LengthSq;
                    if (dist >= maxDist)
                    {
                        maxDist = dist;
                        splitPoint = i;
                    }
                }

                return maxDist;
            }

            private Vec ComputeCenterTangent(int center)
            {
                Vec v1 = _points[center - 1] - _points[center];
                Vec v2 = _points[center] - _points[center + 1];
                var tangent = (v1 + v2) * 0.5;
                if (tangent.LengthSq < 1e-12)
                    tangent = v1;
                return tangent.Normalized();
            }

            private static Vec BezierEval(int degree, Vec[] v, double t)
            {
                var tmp = (Vec[])v.Clone();
                for (int i = 1; i <= degree; i++)
                {
                    for (int j = 0; j <= degree - i; j++)
                        tmp[j] = tmp[j] * (1 - t) + tmp[j + 1] * t;
                }

                return tmp[0];
            }

            private static double B0(double u) => (1 - u) * (1 - u) * (1 - u);
            private static double B1(double u) => 3 * u * (1 - u) * (1 - u);
            private static double B2(double u) => 3 * u * u * (1 - u);
            private static double B3(double u) => u * u * u;
        }

        private readonly struct Vec
        {
            public readonly double X;
            public readonly double Y;

            public Vec(double x, double y)
            {
                X = x;
                Y = y;
            }

            public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
            public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
            public static Vec operator *(Vec a, double s) => new(a.X * s, a.Y * s);

            public double Dot(Vec b) => X * b.X + Y * b.Y;
            public double LengthSq => X * X + Y * Y;
            public double Length => Math.Sqrt(LengthSq);

            public Vec Normalized()
            {
                double len = Length;
                return len > 1e-12 ? new Vec(X / len, Y / len) : new Vec(0, 0);
            }
        }
    }
}
