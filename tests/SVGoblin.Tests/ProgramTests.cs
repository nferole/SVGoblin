using System.Drawing;
using SVGoblin.Tests.Support;

namespace SVGoblin.Tests
{
    /// <summary>
    /// CLI contract tests: exit codes, error messages and dispatch documented
    /// in the README. Main writes to the process-wide console, so these tests
    /// redirect it per call; no other test class touches the console.
    /// </summary>
    public class ProgramTests : IDisposable
    {
        private readonly string _root =
            Path.Combine(Path.GetTempPath(), "SVGoblin.Tests", Guid.NewGuid().ToString("N"));

        public ProgramTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        /// <summary>Saves a small valid PNG under the temp root.</summary>
        private string SavePng(string relativePath)
        {
            string path = Path.Combine(_root, relativePath);
            using var bmp = BitmapFactory.FilledCircle(24, Color.White, Color.Red, 8);
            BitmapFactory.SavePng(bmp, path);
            return path;
        }

        /// <summary>Runs Main with the console captured, restoring it afterwards.</summary>
        private static (int ExitCode, string StdOut, string StdErr) RunMain(params string[] args)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                int exitCode = Program.Main(args);
                return (exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        [Fact]
        public void Main_UnknownFlag_Exit1UsageOnStderr()
        {
            var (exitCode, _, stderr) = RunMain("--bogus");

            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown option: --bogus", stderr);
            Assert.Contains("Usage:", stderr);
        }

        [Fact]
        public void Main_UnknownCurveEngine_Exit1()
        {
            string input = SavePng("logo.png");

            var (exitCode, _, stderr) = RunMain(input, "--output", Path.Combine(_root, "logo.svg"), "--curve-engine", "splines");

            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown curve engine: splines", stderr);
        }

        [Fact]
        public void Main_MissingInput_Exit1()
        {
            string input = Path.Combine(_root, "missing.png");

            var (exitCode, _, stderr) = RunMain(input);

            Assert.Equal(1, exitCode);
            Assert.Contains("Input not found", stderr);
        }

        [Fact]
        public void Main_RecursiveWithFileInput_Exit1()
        {
            string input = SavePng("logo.png");

            var (exitCode, _, stderr) = RunMain(input, "--recursive");

            Assert.Equal(1, exitCode);
            Assert.Contains("--recursive requires a folder input", stderr);
        }

        [Fact]
        public void Main_FolderWithNoPngs_Exit1()
        {
            File.WriteAllText(Path.Combine(_root, "notes.txt"), "decoy");

            var (exitCode, _, stderr) = RunMain(_root);

            Assert.Equal(1, exitCode);
            Assert.Contains("No .png files found", stderr);
        }

        [Fact]
        public void Main_SingleFileSuccess_Exit0WritesSvgAndSummary()
        {
            string input = SavePng("logo.png");
            string output = Path.Combine(_root, "logo.svg");

            var (exitCode, stdout, _) = RunMain(input, "--output", output);

            Assert.Equal(0, exitCode);
            Assert.NotNull(SvgParsing.Parse(File.ReadAllText(output)));
            Assert.Contains($"Vectorized {input} -> {output}", stdout);
            Assert.Contains("color layer(s)", stdout);
        }

        [Fact]
        public void Main_ExplicitCurveEngine_Exit0()
        {
            string input = SavePng("logo.png");
            string output = Path.Combine(_root, "logo.svg");

            var (exitCode, _, _) = RunMain(input, "--output", output, "--curve-engine", "catmullrom");

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(output));
        }

        [Fact]
        public void Main_BatchWithCorruptPng_Exit1ConvertsGoodFiles()
        {
            SavePng("a.png");
            File.WriteAllText(Path.Combine(_root, "bad.png"), "this is not a png");
            SavePng("z.png");

            var (exitCode, stdout, stderr) = RunMain(_root);

            Assert.Equal(1, exitCode);
            Assert.Contains("Converted 2 of 3 file(s)", stdout);
            Assert.Contains("Failed", stderr);
            Assert.True(File.Exists(Path.Combine(_root, "a.svg")));
            Assert.True(File.Exists(Path.Combine(_root, "z.svg")));
            Assert.False(File.Exists(Path.Combine(_root, "bad.svg")));
        }
    }
}
