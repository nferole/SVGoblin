namespace SVGoblin
{
    internal class Program
    {
        internal static int Main(string[] args)
        {
            var positional = new List<string>();
            if (!TryParseArgs(args, positional, out bool recursive, out string? output, out string? curveEngine))
            {
                PrintUsage();
                return 1;
            }

            if (positional.Count == 0)
            {
                Console.Error.WriteLine("Error: input file or folder is required.");
                PrintUsage();
                return 1;
            }

            string input = positional[0];

            var options = new VectorizerOptions();
            if (curveEngine != null && !TryApplyCurveEngine(curveEngine, options))
            {
                Console.Error.WriteLine($"Unknown curve engine: {curveEngine}");
                PrintUsage();
                return 1;
            }

            if (Directory.Exists(input))
            {
                var batch = new BatchOptions
                {
                    InputFolder = input,
                    OutputFolder = output,
                    Recursive = recursive,
                };
                return RunBatch(batch, options);
            }

            if (recursive)
            {
                Console.Error.WriteLine("--recursive requires a folder input.");
                PrintUsage();
                return 1;
            }

            if (!File.Exists(input))
            {
                Console.Error.WriteLine($"Input not found: {input}");
                PrintUsage();
                return 1;
            }

            return RunSingle(input, output ?? "./output.svg", options);
        }

        private static bool TryParseArgs(
            string[] args,
            List<string> positional,
            out bool recursive,
            out string? output,
            out string? curveEngine)
        {
            recursive = false;
            output = null;
            curveEngine = null;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "--recursive")
                {
                    recursive = true;
                }
                else if (arg == "--output")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--output requires a value");
                        return false;
                    }
                    output = args[++i];
                }
                else if (arg == "--curve-engine")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--curve-engine requires a value");
                        return false;
                    }
                    curveEngine = args[++i];
                }
                else if (arg.StartsWith("--"))
                {
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    return false;
                }
                else
                {
                    positional.Add(arg);
                }
            }

            return true;
        }

        private static int RunSingle(string input, string output, VectorizerOptions options)
        {
            var vectorizer = new Vectorizer(options);
            vectorizer.VectorizeToDisk(input, output);
            PrintSummary(input, output, vectorizer.LastStats!);
            return 0;
        }

        private static int RunBatch(BatchOptions batch, VectorizerOptions options)
        {
            var results = new BatchVectorizer(options).Run(batch, PrintFileResult);
            if (results.Count == 0)
            {
                Console.Error.WriteLine($"No .png files found in: {batch.InputFolder}");
                return 1;
            }

            int converted = results.Count(result => result.Succeeded);
            Console.WriteLine($"Converted {converted} of {results.Count} file(s)");
            return converted == results.Count ? 0 : 1;
        }

        private static void PrintFileResult(BatchFileResult result)
        {
            if (result.Succeeded)
                PrintSummary(result.InputPath, result.OutputPath, result.Stats!);
            else
                Console.Error.WriteLine($"Failed {result.InputPath}: {result.Error}");
        }

        /// <summary>Applies a curve engine name from the command line to the options.</summary>
        private static bool TryApplyCurveEngine(string name, VectorizerOptions options)
        {
            switch (name.ToLowerInvariant())
            {
                case "schneider":
                    return true;
                case "catmullrom":
                    options.CurveMode = CurveMode.CatmullRom;
                    return true;
                case "lines":
                    options.EnableCurveFitting = false;
                    return true;
                default:
                    return false;
            }
        }

        private static void PrintUsage()
            => Console.Error.WriteLine(
                "Usage: SVGoblin [input.png|folder] [--output <path>] [--curve-engine schneider|catmullrom|lines] [--recursive]");

        private static void PrintSummary(string input, string output, VectorizeStats stats)
        {
            Console.WriteLine($"Vectorized {input} -> {output}");
            Console.WriteLine($"  {stats.Layers} color layer(s), {stats.Loops} loop(s), {stats.Segments} segment(s)");
        }
    }
}
