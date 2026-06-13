using System.Drawing;
using SVGoblin.Tests.Support;

namespace SVGoblin.Tests
{
    /// <summary>
    /// Golden snapshots of full pipeline output for three synthetic inputs.
    /// These intentionally fail on ANY algorithm change - if the change is
    /// deliberate, regenerate the literals from the new output after checking
    /// the rendered SVGs still look right.
    /// </summary>
    public class SvgSnapshotTests
    {
        private static string Normalize(string svg) => svg.Replace("\r\n", "\n").TrimEnd('\n');

        [Fact]
        public void Snapshot_TwoToneSquare_Lines()
        {
            using var bmp = BitmapFactory.TwoToneSquare(16, Color.White, Color.Red, new Rectangle(4, 4, 8, 8));
            string svg = new Vectorizer(new VectorizerOptions { EnableCurveFitting = false }).Vectorize(bmp);

            const string expected = """
                <?xml version="1.0" encoding="UTF-8"?>
                <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16">
                  <path fill="#FF0000" fill-rule="nonzero" d="M11.63,11.63 L4.38,11.63 L4.38,4.38 L11.63,4.38 L11.63,11.63 Z"/>
                </svg>
                """;
            Assert.Equal(Normalize(expected), Normalize(svg));
        }

        [Fact]
        public void Snapshot_Circle_Schneider()
        {
            using var bmp = BitmapFactory.FilledCircle(32, Color.White, Color.Red, 12);
            string svg = new Vectorizer(new VectorizerOptions { CurveMode = CurveMode.Schneider }).Vectorize(bmp);

            const string expected = """
                <?xml version="1.0" encoding="UTF-8"?>
                <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
                  <path fill="#FF0000" fill-rule="nonzero" d="M10.31,26.69 C-3.51,19.84 7.66,-1.63 21.69,5.31 C35.51,12.16 24.34,33.63 10.31,26.69 Z"/>
                </svg>
                """;
            Assert.Equal(Normalize(expected), Normalize(svg));
        }

        [Fact]
        public void Snapshot_Circle_CatmullRom()
        {
            using var bmp = BitmapFactory.FilledCircle(32, Color.White, Color.Red, 12);
            string svg = new Vectorizer(new VectorizerOptions { CurveMode = CurveMode.CatmullRom }).Vectorize(bmp);

            const string expected = """
                <?xml version="1.0" encoding="UTF-8"?>
                <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
                  <path fill="#FF0000" fill-rule="nonzero" d="M17.94,27.94 C15.86,28.1 12.45,27.78 10.31,26.69 C8.27,25.64 6.34,23.36 5.31,21.69 L4.06,17.94 C3.9,15.86 4.22,12.45 5.31,10.31 C6.36,8.27 8.64,6.34 10.31,5.31 L14.06,4.06 C16.14,3.9 19.55,4.22 21.69,5.31 C23.73,6.36 25.66,8.64 26.69,10.31 L27.94,14.06 C28.1,16.14 27.78,19.55 26.69,21.69 C25.64,23.73 23.36,25.66 21.69,26.69 L17.94,27.94 Z"/>
                </svg>
                """;
            Assert.Equal(Normalize(expected), Normalize(svg));
        }
    }
}
