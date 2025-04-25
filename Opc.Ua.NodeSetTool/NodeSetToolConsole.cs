using McMaster.Extensions.CommandLineUtils;

namespace NodeSetTool
{
    static class NodeSetToolConsole
    {
        public static void Run(string[] args)
        {
            var app = new CommandLineApplication();
            app.Name = "NodeSetTool";
            app.Description = "An tool to convert NodeSets between XML, JSON and GZIP forms.";
            app.HelpOption("-?|-h|--help");

            app.Command("convert", (e) => Convert(e));
            app.Command("compare", (e) => Compare(e));

            app.OnExecute(() =>
            {
                app.ShowHelp();
                return 0;
            });

            app.Execute(args);
        }

        const string JsonFile = "json";
        const string XmlFile = "xml";
        const string ArchiveFile = "tar.gz";

        private static void Convert(CommandLineApplication app)
        {
            app.Description = "Converts a NodeSet from one format to another.";
            app.HelpOption("-?|-h|--help");

            app.Option(
                $"--{OptionsNames.InputFilePath}",
                "The input file path.",
                CommandOptionType.SingleValue);

            app.Option(
                $"--{OptionsNames.OutputFilePath}",
                "The output file path.",
                CommandOptionType.SingleValue);

            app.Option(
                $"--{OptionsNames.OutputFileType}",
                $"The output file type ({JsonFile} | {XmlFile} | {ArchiveFile})",
                CommandOptionType.SingleValue);

            app.Option(
                $"--{OptionsNames.MaxFileSize}",
                $"The maximum number of nodes in a file with an archive ({ArchiveFile} only).",
                CommandOptionType.SingleValue);

            app.OnExecuteAsync((token) =>
            {
                var options = new OptionValues()
                {
                    InputFilePath = app.GetOption(OptionsNames.InputFilePath, null),
                    OutputFilePath = app.GetOption(OptionsNames.OutputFilePath, null),
                    OutputFileType = app.GetOption(OptionsNames.OutputFileType, null),
                    MaxFileSize = app.GetOption(OptionsNames.MaxFileSize, 1000)
                };

                Log.Info("");
                Log.Info("--------------------------------------------------------");
                Log.Info($"Converting NodeSet '{Path.GetFileName(options.InputFilePath)}' to' {Path.GetFileName(options.OutputFilePath)}'.");
                Log.Info("--------------------------------------------------------");

                if (!File.Exists(options.InputFilePath))
                {
                    Log.Error($"File '{options.InputFilePath}' does not exist.");
                    Environment.Exit(1);
                }

                if (String.IsNullOrEmpty(options.OutputFilePath))
                {
                    options.OutputFilePath = options.InputFilePath;
                    options.OutputFilePath = Path.ChangeExtension(options.OutputFilePath, options.OutputFileType);
                }

                var directory = Path.GetDirectoryName(options.OutputFilePath);

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (String.IsNullOrWhiteSpace(options.OutputFileType))
                {
                    Log.Error($"No output file type was provided (choose: {JsonFile} | {XmlFile} | {ArchiveFile}).");
                    Environment.Exit(1);
                }

                options.OutputFileType = options.OutputFileType.ToLower();

                switch (options.OutputFileType)
                {
                    case JsonFile:
                    case XmlFile:
                    case ArchiveFile:
                    {
                        if (options.InputFilePath.EndsWith("." + options.OutputFileType))
                        {
                            Log.Error($"Output file type must be different from the input file type.");
                            Environment.Exit(1);
                        }

                        break;
                    }

                    default:
                    {
                        Log.Error($"Output file type not supported (choose: {JsonFile} | {XmlFile} | {ArchiveFile}).");
                        Environment.Exit(1);
                        break;
                    }
                }

                try
                {
                    var serializer = new NodeSetSerializer();
                    serializer.Load(options.InputFilePath);

                    switch (options.OutputFileType)
                    {
                        case XmlFile:
                        {
                            serializer.SaveXml(options.OutputFilePath);
                            break;
                        }

                        case JsonFile:
                        {
                            serializer.SaveJson(options.OutputFilePath);
                            break;
                        }

                        case ArchiveFile:
                        {
                            serializer.SaveArchive(options.OutputFilePath, Math.Max(options.MaxFileSize, 100));
                            break;
                        }
                    }

                    Log.Info($"The file converted successfully!");
                }
                catch (Exception e)
                {
                    Log.Error($"Conversion failed. ([{e.GetType().Name}] {e.Message})");
                    Environment.Exit(1);
                }

                return Task.CompletedTask;
            });
        }

        private static void Compare(CommandLineApplication app)
        {
            app.Description = "Compares two NodeSets.";
            app.HelpOption("-?|-h|--help");

            app.Option(
                $"--{OptionsNames.InputFilePath}",
                "The input file path.",
                CommandOptionType.SingleValue);

            app.Option(
                $"--{OptionsNames.CompareFilePath}",
                "The comparison target file path.",
                CommandOptionType.SingleValue);

            app.OnExecuteAsync((token) =>
            {
                var options = new OptionValues()
                {
                    InputFilePath = app.GetOption(OptionsNames.InputFilePath, null),
                    CompareFilePath = app.GetOption(OptionsNames.CompareFilePath, null)
                };

                Log.Info("");
                Log.Info("--------------------------------------------------------");
                Log.Info($"Comparing NodeSet '{Path.GetFileName(options.InputFilePath)}' to' {Path.GetFileName(options.CompareFilePath)}'.");
                Log.Info("--------------------------------------------------------");

                if (!File.Exists(options.InputFilePath))
                {
                    Log.Error($"File '{options.InputFilePath}' does not exist.");
                    Environment.Exit(1);
                }

                if (!File.Exists(options.CompareFilePath))
                {
                    Log.Error($"File '{options.CompareFilePath}' does not exist.");
                    Environment.Exit(1);
                }

                var serializer1 = new NodeSetSerializer();
                var serializer2 = new NodeSetSerializer();

                try
                {
                    serializer1.Load(options.InputFilePath);
                }
                catch (Exception e)
                {
                    Log.Error($"File '{options.InputFilePath}' could not be loaded. ([{e.GetType().Name}] {e.Message})");
                    Environment.Exit(1);
                }

                try
                {
                    serializer2.Load(options.CompareFilePath);
                }
                catch (Exception e)
                {
                    Log.Error($"File '{options.CompareFilePath}' could not be loaded. ([{e.GetType().Name}] {e.Message})");
                    Environment.Exit(1);
                }

                if (serializer1.Compare(serializer2))
                {
                    Log.Info($"The two files are the same!");
                    Environment.Exit(0);
                }

                Log.Warning($"The two files are different!");

                foreach (var error in serializer1.CompareErrors)
                {
                    if (error.Original is ValueType || error.Original is string)
                    {
                        Log.Warning($">>> Node={error.Node?.BrowseName} [{error.Node?.NodeId}], Message='{error.Message}' [{error.Original} != {error.Target}].");
                    }
                    else
                    {
                        Log.Warning($">>> Node={error.Node?.BrowseName} [{error.Node?.NodeId}], Message='{error.Message}'.");
                    }
                }

                return Task.CompletedTask;
            });
        }

        private static class OptionsNames
        {
            public const string InputFilePath = "in";
            public const string OutputFilePath = "out";
            public const string OutputFileType = "type";
            public const string CompareFilePath = "target";
            public const string MaxFileSize = "max";
        }

        private class OptionValues
        {
            public string InputFilePath;
            public string OutputFilePath;
            public string OutputFileType;
            public string CompareFilePath;
            public int MaxFileSize;
        }
    }
}
