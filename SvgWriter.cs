using System.Drawing;
using System.Globalization;
using System.Text;

namespace SVGoblin
{
    /// <summary>Serializes traced color layers to an SVG document.</summary>
    internal static class SvgWriter
    {
        public static string Write(List<TracedLayer> layers, int width, int height, VectorizerOptions options)
        {
            var svg = new StringBuilder();
            svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");

            if (!string.IsNullOrEmpty(options.BackgroundColor))
                svg.AppendLine($"  <rect width=\"{width}\" height=\"{height}\" fill=\"{options.BackgroundColor}\"/>");

            foreach (var layer in layers)
                svg.AppendLine($"  <path fill=\"{ToHex(layer.Color)}\" fill-rule=\"nonzero\" d=\"{BuildPathData(layer.Loops)}\"/>");

            svg.AppendLine("</svg>");
            return svg.ToString();
        }

        /// <summary>Builds one layer's path data: one subpath per closed loop.</summary>
        private static string BuildPathData(List<List<PathSegment>> loops)
        {
            var d = new StringBuilder();
            foreach (var loop in loops)
            {
                if (loop.Count == 0)
                    continue;
                if (d.Length > 0)
                    d.Append(' ');

                d.Append('M').Append(Format(loop[0].Start));
                foreach (var segment in loop)
                    AppendSegment(d, segment);

                d.Append(" Z");
            }

            return d.ToString();
        }

        private static void AppendSegment(StringBuilder d, PathSegment segment)
        {
            if (segment.IsCurve)
            {
                d.Append(" C").Append(Format(segment.Control1))
                 .Append(' ').Append(Format(segment.Control2))
                 .Append(' ').Append(Format(segment.End));
            }
            else
            {
                d.Append(" L").Append(Format(segment.End));
            }
        }

        private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static string Format(PointF p)
            => p.X.ToString("0.##", CultureInfo.InvariantCulture) + "," + p.Y.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
