using System.Drawing;

namespace SVGoblin
{
    /// <summary>Smoothing and Ramer-Douglas-Peucker simplification for closed contours.</summary>
    internal static class PathSimplifier
    {
        /// <summary>
        /// Smooths a closed unit-spaced lattice contour with a binomial
        /// [1 2 1]/4 filter. Each pass damps the single-pixel staircase and
        /// quantization wobble while displacing a sharp corner by at most
        /// ~0.35 px, so corners survive but jags average away.
        /// </summary>
        public static List<PointF> SmoothClosed(List<Point> contour, int passes)
        {
            int n = contour.Count;
            var cur = new PointF[n];
            for (int i = 0; i < n; i++)
                cur[i] = new PointF(contour[i].X, contour[i].Y);

            if (n < 3)
                return new List<PointF>(cur);

            var next = new PointF[n];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    PointF a = cur[(i - 1 + n) % n];
                    PointF b = cur[i];
                    PointF c = cur[(i + 1) % n];
                    next[i] = new PointF(
                        0.25f * a.X + 0.5f * b.X + 0.25f * c.X,
                        0.25f * a.Y + 0.5f * b.Y + 0.25f * c.Y);
                }

                (cur, next) = (next, cur);
            }

            return new List<PointF>(cur);
        }

        /// <summary>
        /// RDP-simplifies a closed contour. <paramref name="sourceIndex"/>
        /// maps every kept vertex back to its index in <paramref name="pts"/>
        /// so later stages can test stretches against the full contour.
        /// </summary>
        public static List<PointF> SimplifyClosed(List<PointF> pts, double tolerance, out List<int> sourceIndex)
        {
            int n = pts.Count;
            if (n <= 4 || tolerance <= 0)
            {
                sourceIndex = new List<int>(n);
                for (int i = 0; i < n; i++)
                    sourceIndex.Add(i);
                return new List<PointF>(pts);
            }

            // RDP needs open chains with fixed endpoints; anchor the loop at
            // two mutually distant points and simplify the two halves.
            int a = Farthest(pts, pts[0]);
            int b = Farthest(pts, pts[a]);

            // Rotate so the chain starts at a, and append the start point so
            // the closing stretch b..a is a contiguous open chain too.
            var ext = new List<PointF>(n + 1);
            for (int i = 0; i < n; i++)
                ext.Add(pts[(a + i) % n]);
            ext.Add(pts[a]);
            int bIdx = (b - a + n) % n;

            var keep = new bool[n + 1];
            keep[0] = keep[bIdx] = keep[n] = true;
            double tolSq = tolerance * tolerance;
            Rdp(ext, 0, bIdx, tolSq, keep);
            Rdp(ext, bIdx, n, tolSq, keep);

            var result = new List<PointF>();
            sourceIndex = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (keep[i])
                {
                    result.Add(ext[i]);
                    sourceIndex.Add((a + i) % n);
                }
            }

            return result;
        }

        private static void Rdp(List<PointF> pts, int first, int last, double tolSq, bool[] keep)
        {
            if (last <= first + 1)
                return;

            double maxD = -1;
            int maxI = first + 1;
            for (int i = first + 1; i < last; i++)
            {
                double d = SegmentDistSq(pts[i], pts[first], pts[last]);
                if (d > maxD)
                {
                    maxD = d;
                    maxI = i;
                }
            }

            if (maxD > tolSq)
            {
                keep[maxI] = true;
                Rdp(pts, first, maxI, tolSq, keep);
                Rdp(pts, maxI, last, tolSq, keep);
            }
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

        private static int Farthest(List<PointF> pts, PointF from)
        {
            int best = 0;
            double bestD = -1;
            for (int i = 0; i < pts.Count; i++)
            {
                double dx = pts[i].X - from.X, dy = pts[i].Y - from.Y;
                double d = dx * dx + dy * dy;
                if (d > bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }
    }
}
