using System.Drawing;

namespace SVGoblin
{
    /// <summary>
    /// Extracts closed boundary polygons from a binary mask by walking the
    /// "cracks" between foreground and background pixels. Points are lattice
    /// coordinates (pixel corners), so polygons bound the region exactly.
    /// Loops keep every unit step of the walk (collinear points included) so
    /// the smoothing stage sees evenly spaced points.
    /// Outer boundaries and holes come out with opposite winding, which makes
    /// a nonzero fill rule render holes correctly.
    /// </summary>
    internal sealed class ContourTracer
    {
        // Directions: 0 = E, 1 = S, 2 = W, 3 = N (y grows downward).
        private const int East = 0;
        private static readonly int[] Dx = { 1, 0, -1, 0 };
        private static readonly int[] Dy = { 0, 1, 0, -1 };

        private readonly byte[] _mask;
        private readonly int _width;
        private readonly int _height;

        // Every loop (outer or hole) contains at least one east-heading crack
        // with foreground below and background above, so those edges are
        // sufficient both for seeding and for de-duplication.
        private readonly bool[] _visitedEast;

        public static List<List<Point>> Trace(byte[] mask, int width, int height)
            => new ContourTracer(mask, width, height).TraceAll();

        public static double SignedArea(List<Point> polygon)
        {
            long sum = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point a = polygon[i];
                Point b = polygon[(i + 1) % polygon.Count];
                sum += (long)a.X * b.Y - (long)b.X * a.Y;
            }

            return sum / 2.0;
        }

        private ContourTracer(byte[] mask, int width, int height)
        {
            _mask = mask;
            _width = width;
            _height = height;
            _visitedEast = new bool[mask.Length];
        }

        private List<List<Point>> TraceAll()
        {
            var contours = new List<List<Point>>();
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    if (StartsUntracedLoop(x, y))
                        contours.Add(TraceLoop(x, y));
                }
            }

            return contours;
        }

        /// <summary>
        /// Whether a new loop should be seeded at this pixel: foreground with
        /// background above it, whose east-heading crack has not been walked.
        /// </summary>
        private bool StartsUntracedLoop(int x, int y)
        {
            int idx = y * _width + x;
            if (_mask[idx] != 1 || _visitedEast[idx])
                return false;

            return y == 0 || _mask[idx - _width] == 0;
        }

        private List<Point> TraceLoop(int startX, int startY)
        {
            var raw = new List<Point>();
            int x = startX;
            int y = startY;
            int dir = East; // East along the crack above the seed pixel.
            long guard = 4L * _mask.Length + 8;

            do
            {
                if (dir == East)
                    _visitedEast[y * _width + x] = true;
                raw.Add(new Point(x, y));

                x += Dx[dir];
                y += Dy[dir];

                var (aheadLeft, aheadRight) = PixelsAhead(x, y, dir);

                // Keep foreground on the right of the walk. At diagonal
                // "checkerboard" junctions (aheadLeft set, aheadRight clear)
                // turning left treats the foreground as 8-connected, so thin
                // diagonal strokes stay in one piece.
                if (aheadLeft)
                    dir = (dir + 3) & 3;
                else if (!aheadRight)
                    dir = (dir + 1) & 3;
            }
            while ((x != startX || y != startY || dir != East) && --guard > 0);

            return raw;
        }

        /// <summary>The two pixels ahead of the lattice point, relative to the travel direction.</summary>
        private (bool Left, bool Right) PixelsAhead(int x, int y, int dir) => dir switch
        {
            0 => (IsSet(x, y - 1), IsSet(x, y)),
            1 => (IsSet(x, y), IsSet(x - 1, y)),
            2 => (IsSet(x - 1, y), IsSet(x - 1, y - 1)),
            _ => (IsSet(x - 1, y - 1), IsSet(x, y - 1)),
        };

        private bool IsSet(int x, int y)
            => x >= 0 && x < _width && y >= 0 && y < _height && _mask[y * _width + x] == 1;
    }
}
