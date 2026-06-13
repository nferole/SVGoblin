using System.Drawing;

namespace SVGoblin
{
    /// <summary>A flat color layer: the quantized color and its binary pixel mask.</summary>
    internal sealed class ColorLayer
    {
        public Color Color { get; }
        public byte[] Mask { get; }
        public int PixelCount { get; set; }

        public ColorLayer(Color color, int size)
        {
            Color = color;
            Mask = new byte[size];
        }
    }

    /// <summary>
    /// Groups an image's opaque, non-background pixels into a small set of flat
    /// colors and produces a binary mask per color. Anti-aliased edge pixels are
    /// excluded while building the palette (they sit on color ramps, so their
    /// neighborhood is not flat) and are then assigned to whichever palette
    /// color or background is nearest, which splits the ramps cleanly.
    /// </summary>
    internal sealed class ColorQuantizer
    {
        private const int MaxInitialClusters = 64;
        private const int MinClusterPixels = 50;
        private const int NeighborSimilaritySq = 16 * 16;

        // Anti-aliasing ramps on large images are several pixels wide, so
        // flatness must be sampled past them rather than at adjacent pixels.
        private const int FlatRadius = 3;

        private readonly uint[] _pixels;
        private readonly int _width;
        private readonly int _height;
        private readonly VectorizerOptions _options;
        private readonly double _toleranceSq;
        private readonly (double R, double G, double B)? _background;

        public static List<ColorLayer> Quantize(uint[] pixels, int width, int height, VectorizerOptions options)
            => new ColorQuantizer(pixels, width, height, options).Quantize();

        private ColorQuantizer(uint[] pixels, int width, int height, VectorizerOptions options)
        {
            _pixels = pixels;
            _width = width;
            _height = height;
            _options = options;
            _toleranceSq = (double)options.ColorTolerance * options.ColorTolerance;
            _background = DetectBackground();
        }

        private List<ColorLayer> Quantize()
        {
            var clusters = BuildClusters(requireFlatNeighborhood: true);
            if (clusters.Count == 0)
                clusters = BuildClusters(requireFlatNeighborhood: false);
            if (clusters.Count == 0)
                return new List<ColorLayer>();

            MergeClusters(clusters);
            return BuildLayers(clusters);
        }

        /// <summary>
        /// Estimates the background color by sampling the image corners. Returns
        /// null when the corners are transparent (alpha is the background).
        /// </summary>
        private (double R, double G, double B)? DetectBackground()
        {
            uint[] corners =
            {
                _pixels[0],
                _pixels[_width - 1],
                _pixels[(_height - 1) * _width],
                _pixels[_height * _width - 1],
            };

            var opaque = corners.Where(Rgba.IsOpaque).ToArray();
            if (opaque.Length < 3)
                return null;

            return (
                opaque.Average(c => (double)Rgba.Red(c)),
                opaque.Average(c => (double)Rgba.Green(c)),
                opaque.Average(c => (double)Rgba.Blue(c)));
        }

        private List<Cluster> BuildClusters(bool requireFlatNeighborhood)
        {
            var clusters = new List<Cluster>();

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    if (IsPaletteCandidate(x, y, requireFlatNeighborhood))
                        AddToNearestCluster(clusters, _pixels[y * _width + x]);
                }
            }

            return clusters;
        }

        /// <summary>
        /// Whether a pixel may seed or grow the palette: opaque, distinct from
        /// the background and, when required, sitting in a flat-colored
        /// neighborhood (anti-aliased edge pixels sit on color ramps).
        /// </summary>
        private bool IsPaletteCandidate(int x, int y, bool requireFlatNeighborhood)
        {
            uint pixel = _pixels[y * _width + x];
            if (!Rgba.IsOpaque(pixel))
                return false;

            if (_background.HasValue && DistSq(pixel, _background.Value) < _toleranceSq)
                return false;

            return !requireFlatNeighborhood || HasFlatNeighborhood(x, y);
        }

        /// <summary>
        /// Adds the pixel to its nearest cluster, or seeds a new cluster when
        /// no cluster is close enough and the initial cap is not yet reached.
        /// </summary>
        private void AddToNearestCluster(List<Cluster> clusters, uint pixel)
        {
            int nearest = -1;
            double nearestD = double.MaxValue;
            for (int k = 0; k < clusters.Count; k++)
            {
                double d = DistSq(pixel, clusters[k].Mean);
                if (d < nearestD)
                {
                    nearestD = d;
                    nearest = k;
                }
            }

            if (nearest >= 0 && (nearestD < _toleranceSq || clusters.Count >= MaxInitialClusters))
                clusters[nearest].Add(pixel);
            else
                clusters.Add(Cluster.From(pixel));
        }

        private bool HasFlatNeighborhood(int x, int y)
        {
            uint p = _pixels[y * _width + x];
            int similar = 0;
            int r = FlatRadius;
            if (x >= r && PixelDistSq(p, _pixels[y * _width + x - r]) <= NeighborSimilaritySq) similar++;
            if (x < _width - r && PixelDistSq(p, _pixels[y * _width + x + r]) <= NeighborSimilaritySq) similar++;
            if (y >= r && PixelDistSq(p, _pixels[(y - r) * _width + x]) <= NeighborSimilaritySq) similar++;
            if (y < _height - r && PixelDistSq(p, _pixels[(y + r) * _width + x]) <= NeighborSimilaritySq) similar++;
            return similar >= 3;
        }

        private void MergeClusters(List<Cluster> clusters)
        {
            // Fold tiny clusters (anti-aliasing remnants, noise) into their nearest neighbor.
            while (clusters.Count > 1)
            {
                int tiny = clusters.FindIndex(c => c.Count < MinClusterPixels);
                if (tiny < 0)
                    break;
                Absorb(clusters, tiny, NearestOther(clusters, tiny));
            }

            // Reduce to the requested palette size by merging the closest pair.
            while (clusters.Count > Math.Max(1, _options.MaxColors))
            {
                var (i, j, _) = ClosestPair(clusters);
                Absorb(clusters, j, i);
            }

            // Merge palette entries within the color tolerance of each other -
            // they would fight over the same pixels and fragment the masks.
            while (clusters.Count > 1)
            {
                var (i, j, distSq) = ClosestPair(clusters);
                if (distSq >= _toleranceSq)
                    break;
                Absorb(clusters, j, i);
            }
        }

        /// <summary>
        /// Creates one mask layer per cluster and assigns every opaque pixel to
        /// the nearest of {background, palette colors}.
        /// </summary>
        private List<ColorLayer> BuildLayers(List<Cluster> clusters)
        {
            var layers = new List<ColorLayer>(clusters.Count);
            foreach (var cluster in clusters)
                layers.Add(new ColorLayer(cluster.ToColor(), _pixels.Length));

            for (int i = 0; i < _pixels.Length; i++)
            {
                if (!Rgba.IsOpaque(_pixels[i]))
                    continue;

                int nearest = NearestPaletteIndex(_pixels[i], clusters);
                if (nearest >= 0)
                {
                    layers[nearest].Mask[i] = 1;
                    layers[nearest].PixelCount++;
                }
            }

            layers.RemoveAll(l => l.PixelCount < Math.Max(1, _options.MinShapeArea));
            return layers;
        }

        /// <summary>
        /// Index of the cluster nearest to the pixel, or -1 when the background
        /// color is nearer than every cluster.
        /// </summary>
        private int NearestPaletteIndex(uint pixel, List<Cluster> clusters)
        {
            double best = _background.HasValue ? DistSq(pixel, _background.Value) : double.MaxValue;
            int bestCluster = -1;
            for (int k = 0; k < clusters.Count; k++)
            {
                double d = DistSq(pixel, clusters[k].Mean);
                if (d < best)
                {
                    best = d;
                    bestCluster = k;
                }
            }

            return bestCluster;
        }

        private static (int I, int J, double DistSq) ClosestPair(List<Cluster> clusters)
        {
            int bestI = 0, bestJ = 1;
            double bestD = double.MaxValue;
            for (int i = 0; i < clusters.Count; i++)
            {
                for (int j = i + 1; j < clusters.Count; j++)
                {
                    double d = CenterDistSq(clusters[i], clusters[j]);
                    if (d < bestD)
                    {
                        bestD = d;
                        bestI = i;
                        bestJ = j;
                    }
                }
            }

            return (bestI, bestJ, bestD);
        }

        private static void Absorb(List<Cluster> clusters, int source, int target)
        {
            clusters[target].Absorb(clusters[source]);
            clusters.RemoveAt(source);
        }

        private static int NearestOther(List<Cluster> clusters, int index)
        {
            int best = -1;
            double bestD = double.MaxValue;
            for (int k = 0; k < clusters.Count; k++)
            {
                if (k == index)
                    continue;
                double d = CenterDistSq(clusters[index], clusters[k]);
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }

            return best;
        }

        private static double CenterDistSq(Cluster a, Cluster b)
        {
            double dr = a.MeanR - b.MeanR;
            double dg = a.MeanG - b.MeanG;
            double db = a.MeanB - b.MeanB;
            return dr * dr + dg * dg + db * db;
        }

        private static double DistSq(uint p, (double R, double G, double B) c)
        {
            double dr = Rgba.Red(p) - c.R;
            double dg = Rgba.Green(p) - c.G;
            double db = Rgba.Blue(p) - c.B;
            return dr * dr + dg * dg + db * db;
        }

        private static int PixelDistSq(uint a, uint b)
        {
            int dr = Rgba.Red(a) - Rgba.Red(b);
            int dg = Rgba.Green(a) - Rgba.Green(b);
            int db = Rgba.Blue(a) - Rgba.Blue(b);
            return dr * dr + dg * dg + db * db;
        }

        private sealed class Cluster
        {
            private double _sumR, _sumG, _sumB;

            public int Count { get; private set; }
            public double MeanR => _sumR / Count;
            public double MeanG => _sumG / Count;
            public double MeanB => _sumB / Count;
            public (double R, double G, double B) Mean => (MeanR, MeanG, MeanB);

            public static Cluster From(uint p)
            {
                var cluster = new Cluster();
                cluster.Add(p);
                return cluster;
            }

            public void Add(uint p)
            {
                _sumR += Rgba.Red(p);
                _sumG += Rgba.Green(p);
                _sumB += Rgba.Blue(p);
                Count++;
            }

            public void Absorb(Cluster other)
            {
                _sumR += other._sumR;
                _sumG += other._sumG;
                _sumB += other._sumB;
                Count += other.Count;
            }

            public Color ToColor() => Color.FromArgb(
                (int)Math.Round(MeanR),
                (int)Math.Round(MeanG),
                (int)Math.Round(MeanB));
        }
    }
}
