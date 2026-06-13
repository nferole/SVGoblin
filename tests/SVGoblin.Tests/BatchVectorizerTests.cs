using System.Drawing;
using SVGoblin.Tests.Support;

namespace SVGoblin.Tests
{
    /// <summary>
    /// Batch conversion: pure output-path mapping plus Run() behavior against a
    /// real temp folder (enumeration, mirroring, per-file failure isolation).
    /// </summary>
    public class BatchVectorizerTests : IDisposable
    {
        private readonly string _root =
            Path.Combine(Path.GetTempPath(), "SVGoblin.Tests", Guid.NewGuid().ToString("N"));

        public BatchVectorizerTests()
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
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var bmp = BitmapFactory.FilledCircle(24, Color.White, Color.Red, 8);
            BitmapFactory.SavePng(bmp, path);
            return path;
        }

        private BatchOptions Options(string? outputFolder = null, bool recursive = false) => new()
        {
            InputFolder = _root,
            OutputFolder = outputFolder,
            Recursive = recursive,
        };

        // --- GetOutputPath mapping (no IO) ------------------------------------

        [Fact]
        public void GetOutputPath_NoOutputFolder_SvgNextToSource()
        {
            var batch = new BatchOptions { InputFolder = @"C:\in" };

            Assert.Equal(@"C:\in\logo.svg", BatchVectorizer.GetOutputPath(@"C:\in\logo.png", batch));
        }

        [Fact]
        public void GetOutputPath_NoOutputFolderRecursive_SvgNextToNestedSource()
        {
            var batch = new BatchOptions { InputFolder = @"C:\in", Recursive = true };

            Assert.Equal(@"C:\in\sub\a.svg", BatchVectorizer.GetOutputPath(@"C:\in\sub\a.png", batch));
        }

        [Fact]
        public void GetOutputPath_WithOutputFolder_FlatMapping()
        {
            var batch = new BatchOptions { InputFolder = @"C:\in", OutputFolder = @"C:\out" };

            Assert.Equal(@"C:\out\logo.svg", BatchVectorizer.GetOutputPath(@"C:\in\logo.png", batch));
        }

        [Fact]
        public void GetOutputPath_WithOutputFolderRecursive_MirrorsSubfolders()
        {
            var batch = new BatchOptions { InputFolder = @"C:\in", OutputFolder = @"C:\out", Recursive = true };

            Assert.Equal(@"C:\out\sub\deep\a.svg", BatchVectorizer.GetOutputPath(@"C:\in\sub\deep\a.png", batch));
        }

        [Fact]
        public void GetOutputPath_UppercaseExtension_StillMapsToLowercaseSvg()
        {
            var batch = new BatchOptions { InputFolder = @"C:\in" };

            Assert.Equal(@"C:\in\LOGO.svg", BatchVectorizer.GetOutputPath(@"C:\in\LOGO.PNG", batch));
        }

        // --- Run (temp-dir IO) -------------------------------------------------

        [Fact]
        public void Run_FolderWithPngs_WritesSvgPerFileNextToSource()
        {
            SavePng("a.png");
            SavePng("b.png");

            var results = new BatchVectorizer().Run(Options());

            Assert.Equal(2, results.Count);
            Assert.All(results, result => Assert.True(result.Succeeded));
            Assert.All(results, result => Assert.NotNull(result.Stats));
            foreach (var name in new[] { "a.svg", "b.svg" })
                Assert.NotNull(SvgParsing.Parse(File.ReadAllText(Path.Combine(_root, name))));
        }

        [Fact]
        public void Run_TopLevelDefault_IgnoresSubfolderPng()
        {
            SavePng("a.png");
            SavePng(Path.Combine("sub", "c.png"));

            var results = new BatchVectorizer().Run(Options());

            var result = Assert.Single(results);
            Assert.EndsWith("a.png", result.InputPath);
            Assert.False(File.Exists(Path.Combine(_root, "sub", "c.svg")));
        }

        [Fact]
        public void Run_Recursive_ConvertsSubfolderAndMirrorsIntoOutputFolder()
        {
            SavePng("a.png");
            SavePng(Path.Combine("sub", "c.png"));
            string outputFolder = Path.Combine(_root, "out");

            var results = new BatchVectorizer().Run(Options(outputFolder, recursive: true));

            Assert.Equal(2, results.Count);
            Assert.True(File.Exists(Path.Combine(outputFolder, "a.svg")));
            Assert.True(File.Exists(Path.Combine(outputFolder, "sub", "c.svg")));
        }

        [Fact]
        public void Run_MissingOutputFolder_CreatesIt()
        {
            SavePng("a.png");
            string outputFolder = Path.Combine(_root, "does", "not", "exist");

            new BatchVectorizer().Run(Options(outputFolder));

            Assert.True(File.Exists(Path.Combine(outputFolder, "a.svg")));
        }

        [Fact]
        public void Run_CorruptPng_ReportsFailureAndContinues()
        {
            SavePng("a.png");
            File.WriteAllText(Path.Combine(_root, "bad.png"), "this is not a png");
            SavePng("z.png");

            var results = new BatchVectorizer().Run(Options());

            Assert.Equal(3, results.Count);
            var failed = Assert.Single(results, result => !result.Succeeded);
            Assert.EndsWith("bad.png", failed.InputPath);
            Assert.NotNull(failed.Error);
            Assert.False(File.Exists(Path.Combine(_root, "bad.svg")));
            Assert.True(File.Exists(Path.Combine(_root, "a.svg")));
            Assert.True(File.Exists(Path.Combine(_root, "z.svg")));
        }

        [Fact]
        public void Run_EmptyFolder_ReturnsNoResults()
        {
            File.WriteAllText(Path.Combine(_root, "notes.txt"), "decoy");

            var results = new BatchVectorizer().Run(Options());

            Assert.Empty(results);
            Assert.Empty(Directory.GetFiles(_root, "*.svg", SearchOption.AllDirectories));
        }

        [Fact]
        public void Run_NonPngFilesPresent_AreIgnored()
        {
            SavePng("a.png");
            File.WriteAllText(Path.Combine(_root, "notes.txt"), "decoy");
            File.WriteAllText(Path.Combine(_root, "image.jpg"), "decoy");

            var results = new BatchVectorizer().Run(Options());

            var result = Assert.Single(results);
            Assert.EndsWith("a.png", result.InputPath);
            Assert.Equal(new[] { Path.Combine(_root, "a.svg") }, Directory.GetFiles(_root, "*.svg"));
        }

        [Fact]
        public void Run_Results_AreSortedByPathOrdinalIgnoreCase()
        {
            SavePng("b.png");
            SavePng("A.png");

            var results = new BatchVectorizer().Run(Options());

            Assert.Equal(2, results.Count);
            Assert.EndsWith("A.png", results[0].InputPath);
            Assert.EndsWith("b.png", results[1].InputPath);
        }

        [Fact]
        public void Run_OnFileDoneCallback_InvokedOncePerFile()
        {
            SavePng("a.png");
            SavePng("b.png");
            var seen = new List<BatchFileResult>();

            var results = new BatchVectorizer().Run(Options(), seen.Add);

            Assert.Equal(results, seen);
        }
    }
}
