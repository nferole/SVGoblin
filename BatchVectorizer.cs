namespace SVGoblin
{
    /// <summary>
    /// Converts every .png file in a folder to an .svg with the same base name,
    /// reusing one <see cref="Vectorizer"/> across all files.
    /// </summary>
    public sealed class BatchVectorizer
    {
        private readonly Vectorizer _vectorizer;

        public BatchVectorizer(VectorizerOptions? options = null)
        {
            _vectorizer = new Vectorizer(options);
        }

        /// <summary>
        /// Converts every .png in the input folder. A file that fails to convert
        /// is recorded in its result; the batch always runs to completion.
        /// </summary>
        /// <param name="batch">What to convert and where the output goes.</param>
        /// <param name="onFileDone">Optional progress callback, invoked after each file.</param>
        public List<BatchFileResult> Run(BatchOptions batch, Action<BatchFileResult>? onFileDone = null)
        {
            var results = new List<BatchFileResult>();
            foreach (var inputPath in FindInputFiles(batch))
            {
                var result = ConvertFile(inputPath, batch);
                results.Add(result);
                onFileDone?.Invoke(result);
            }

            return results;
        }

        private static IEnumerable<string> FindInputFiles(BatchOptions batch)
        {
            var depth = batch.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(batch.InputFolder, "*.png", depth)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        private BatchFileResult ConvertFile(string inputPath, BatchOptions batch)
        {
            string outputPath = GetOutputPath(inputPath, batch);
            try
            {
                EnsureOutputFolderExists(outputPath);
                _vectorizer.VectorizeToDisk(inputPath, outputPath);
                return new BatchFileResult
                {
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    Stats = _vectorizer.LastStats,
                };
            }
            catch (Exception ex)
            {
                return new BatchFileResult
                {
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    Error = ex.Message,
                };
            }
        }

        /// <summary>Maps a source .png path to its .svg destination.</summary>
        internal static string GetOutputPath(string pngPath, BatchOptions batch)
        {
            if (batch.OutputFolder == null)
                return Path.ChangeExtension(pngPath, ".svg");

            string relative = Path.GetRelativePath(batch.InputFolder, pngPath);
            return Path.ChangeExtension(Path.Combine(batch.OutputFolder, relative), ".svg");
        }

        private static void EnsureOutputFolderExists(string outputPath)
        {
            string? folder = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
        }
    }

    /// <summary>Settings for one folder batch conversion.</summary>
    public sealed class BatchOptions
    {
        /// <summary>Folder whose .png files are converted.</summary>
        public required string InputFolder { get; init; }

        /// <summary>
        /// Folder the .svg files are written to, mirroring the input folder's
        /// subfolder structure. Null writes each .svg next to its source .png.
        /// </summary>
        public string? OutputFolder { get; init; }

        /// <summary>Whether .png files in subfolders are included.</summary>
        public bool Recursive { get; init; }
    }

    /// <summary>Outcome of converting one file in a batch.</summary>
    public sealed class BatchFileResult
    {
        public required string InputPath { get; init; }
        public required string OutputPath { get; init; }

        /// <summary>Statistics for the converted file; null when it failed.</summary>
        public VectorizeStats? Stats { get; init; }

        /// <summary>Error message when the file failed to convert; null on success.</summary>
        public string? Error { get; init; }

        public bool Succeeded => Error == null;
    }
}
