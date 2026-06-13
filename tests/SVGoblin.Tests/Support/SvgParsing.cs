using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SVGoblin.Tests.Support
{
    /// <summary>Parsing of the writer's SVG output so tests can assert on emitted geometry.</summary>
    internal static class SvgParsing
    {
        private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";
        private static readonly Regex NumberPattern = new(@"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        public static XDocument Parse(string svg) => XDocument.Parse(svg);

        public static List<string> PathData(string svg)
            => Parse(svg).Descendants(Ns + "path").Select(p => (string)p.Attribute("d")!).ToList();

        public static List<double> ExtractPathNumbers(string svg)
        {
            var numbers = new List<double>();
            foreach (var d in PathData(svg))
            {
                foreach (Match m in NumberPattern.Matches(d))
                    numbers.Add(double.Parse(m.Value, CultureInfo.InvariantCulture));
            }

            return numbers;
        }

        /// <summary>
        /// The "0.##" format renders double.NaN as the literal string "NaN" and
        /// infinities as the infinity sign, so checking the raw text catches the
        /// actual real-world failure mode.
        /// </summary>
        public static void AssertNoNonFiniteTokens(string svg)
        {
            Assert.DoesNotContain("NaN", svg);
            Assert.DoesNotContain("Infinity", svg);
            Assert.DoesNotContain("∞", svg);
            foreach (var n in ExtractPathNumbers(svg))
                Assert.True(double.IsFinite(n), $"Non-finite number in path data: {n}");
        }

        public static RectangleF ParseViewBox(string svg)
        {
            var vb = (string)Parse(svg).Root!.Attribute("viewBox")!;
            var parts = vb.Split(' ').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            return new RectangleF(parts[0], parts[1], parts[2], parts[3]);
        }

        /// <summary>
        /// Tokenizes all path data into closed loops of segments (the writer
        /// only emits absolute M, L, C and Z), reusing PathSegment so loops feed
        /// straight into GeometryAsserts.
        /// </summary>
        public static List<List<PathSegment>> ParseLoops(string svg)
        {
            var loops = new List<List<PathSegment>>();
            foreach (var d in PathData(svg))
            {
                if (string.IsNullOrWhiteSpace(d))
                    continue;

                var tokens = d.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                List<PathSegment>? loop = null;
                PointF current = default;
                PointF loopStart = default;

                for (int i = 0; i < tokens.Length; i++)
                {
                    string t = tokens[i];
                    switch (t[0])
                    {
                        case 'M':
                            loop = new List<PathSegment>();
                            loops.Add(loop);
                            loopStart = current = ParsePoint(t[1..]);
                            break;
                        case 'L':
                            var end = ParsePoint(t[1..]);
                            loop!.Add(PathSegment.Line(current, end));
                            current = end;
                            break;
                        case 'C':
                            var c1 = ParsePoint(t[1..]);
                            var c2 = ParsePoint(tokens[++i]);
                            var p3 = ParsePoint(tokens[++i]);
                            loop!.Add(PathSegment.Curve(current, c1, c2, p3));
                            current = p3;
                            break;
                        case 'Z':
                            if (current != loopStart)
                                loop!.Add(PathSegment.Line(current, loopStart));
                            current = loopStart;
                            break;
                        default:
                            throw new FormatException($"Unexpected path token '{t}' in: {d}");
                    }
                }
            }

            return loops;
        }

        private static PointF ParsePoint(string token)
        {
            int comma = token.IndexOf(',');
            return new PointF(
                float.Parse(token[..comma], CultureInfo.InvariantCulture),
                float.Parse(token[(comma + 1)..], CultureInfo.InvariantCulture));
        }
    }
}
