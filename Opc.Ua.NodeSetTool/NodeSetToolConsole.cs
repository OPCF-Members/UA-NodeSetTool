using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace NodeSetTool
{
    static class NodeSetToolConsole
    {
        const string JsonFile = "json";
        const string XmlFile = "xml";
        const string ArchiveFile = "tar.gz";

        static ILogger _logger;

        public static void Run(string[] args, ILogger logger)
        {
            _logger = logger;

            var rootCommand = new RootCommand("A tool to convert NodeSets between XML, JSON and GZIP forms.");

            rootCommand.AddCommand(BuildConvertCommand());
            rootCommand.AddCommand(BuildCompareCommand());

            rootCommand.Invoke(args);
        }

        private static Command BuildConvertCommand()
        {
            var inputOption = new Option<string>("--in", "The input file path.");
            var outputOption = new Option<string>("--out", "The output file path.");
            var typeOption = new Option<string>("--type", $"The output file type ({JsonFile} | {XmlFile} | {ArchiveFile}).");
            var maxOption = new Option<int>("--max", () => 1000, $"The maximum number of nodes in a file within an archive ({ArchiveFile} only).");

            var command = new Command("convert", "Converts a NodeSet from one format to another.");
            command.AddOption(inputOption);
            command.AddOption(outputOption);
            command.AddOption(typeOption);
            command.AddOption(maxOption);

            command.SetHandler((inputFilePath, outputFilePath, outputFileType, maxFileSize) =>
            {
                _logger.LogInformation("");
                _logger.LogInformation("--------------------------------------------------------");
                _logger.LogInformation("Converting NodeSet '{InputFile}' to '{OutputFile}'.", Path.GetFileName(inputFilePath), Path.GetFileName(outputFilePath));
                _logger.LogInformation("--------------------------------------------------------");

                if (!File.Exists(inputFilePath))
                {
                    _logger.LogError("File '{FilePath}' does not exist.", inputFilePath);
                    Environment.Exit(1);
                }

                if (String.IsNullOrEmpty(outputFilePath))
                {
                    outputFilePath = Path.ChangeExtension(inputFilePath, outputFileType);
                }

                var directory = Path.GetDirectoryName(outputFilePath);

                if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (String.IsNullOrWhiteSpace(outputFileType))
                {
                    _logger.LogError("No output file type was provided (choose: {JsonFile} | {XmlFile} | {ArchiveFile}).", JsonFile, XmlFile, ArchiveFile);
                    Environment.Exit(1);
                }

                outputFileType = outputFileType.ToLower();

                switch (outputFileType)
                {
                    case JsonFile:
                    case XmlFile:
                    case ArchiveFile:
                        {
                            if (inputFilePath.EndsWith("." + outputFileType))
                            {
                                _logger.LogError("Output file type must be different from the input file type.");
                                Environment.Exit(1);
                            }

                            break;
                        }

                    default:
                        {
                            _logger.LogError("Output file type not supported (choose: {JsonFile} | {XmlFile} | {ArchiveFile}).", JsonFile, XmlFile, ArchiveFile);
                            Environment.Exit(1);
                            break;
                        }
                }

                try
                {
                    var serializer = new NodeSetSerializer();
                    serializer.Load(inputFilePath);

                    switch (outputFileType)
                    {
                        case XmlFile:
                            {
                                serializer.SaveXml(outputFilePath);
                                break;
                            }

                        case JsonFile:
                            {
                                serializer.SaveJson(outputFilePath);
                                break;
                            }

                        case ArchiveFile:
                            {
                                serializer.SaveArchive(outputFilePath, Math.Max(maxFileSize, 100));
                                break;
                            }
                    }

                    _logger.LogInformation("The file converted successfully!");
                }
                catch (Exception e)
                {
                    _logger.LogError("Conversion failed. ([{ExceptionType}] {Message})", e.GetType().Name, e.Message);
                    Environment.Exit(1);
                }
            },
            inputOption, outputOption, typeOption, maxOption);

            return command;
        }

        private static Command BuildCompareCommand()
        {
            var inputOption = new Option<string>("--in", "The input file path.");
            var targetOption = new Option<string>("--target", "The comparison target file path.");

            var command = new Command("compare", "Compares two NodeSets.");
            command.AddOption(inputOption);
            command.AddOption(targetOption);

            command.SetHandler((inputFilePath, compareFilePath) =>
            {
                _logger.LogInformation("");
                _logger.LogInformation("--------------------------------------------------------");
                _logger.LogInformation("Comparing NodeSet '{InputFile}' to '{CompareFile}'.", Path.GetFileName(inputFilePath), Path.GetFileName(compareFilePath));
                _logger.LogInformation("--------------------------------------------------------");

                if (!File.Exists(inputFilePath))
                {
                    _logger.LogError("File '{FilePath}' does not exist.", inputFilePath);
                    Environment.Exit(1);
                }

                if (!File.Exists(compareFilePath))
                {
                    _logger.LogError("File '{FilePath}' does not exist.", compareFilePath);
                    Environment.Exit(1);
                }

                var serializer1 = new NodeSetSerializer();
                var serializer2 = new NodeSetSerializer();

                try
                {
                    serializer1.Load(inputFilePath);
                }
                catch (Exception e)
                {
                    _logger.LogError("File '{FilePath}' could not be loaded. ([{ExceptionType}] {Message})", inputFilePath, e.GetType().Name, e.Message);
                    Environment.Exit(1);
                }

                try
                {
                    serializer2.Load(compareFilePath);
                }
                catch (Exception e)
                {
                    _logger.LogError("File '{FilePath}' could not be loaded. ([{ExceptionType}] {Message})", compareFilePath, e.GetType().Name, e.Message);
                    Environment.Exit(1);
                }

                if (serializer1.Compare(serializer2))
                {
                    _logger.LogInformation("The two files are the same!");
                    return;
                }

                _logger.LogWarning("The two files are different!");

                foreach (var error in serializer1.CompareErrors)
                {
                    if (error.Original is ValueType || error.Original is string)
                    {
                        _logger.LogWarning(">>> Node={BrowseName} [{NodeId}], Message='{ErrorMessage}' [{Original} != {Target}].", error.Node?.BrowseName, error.Node?.NodeId, error.Message, error.Original, error.Target);
                    }
                    else
                    {
                        _logger.LogWarning(">>> Node={BrowseName} [{NodeId}], Message='{ErrorMessage}'.", error.Node?.BrowseName, error.Node?.NodeId, error.Message);
                    }
                }
            },
            inputOption, targetOption);

            return command;
        }
    }
}
