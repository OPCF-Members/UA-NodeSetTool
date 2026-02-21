using NodeSetTool;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Opc.Ua.NodeSetTool.Tests
{
    public class NodeSetConversionTests : IDisposable
    {
        private static readonly string NodeSetsDir =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "nodesets"));

        private static readonly XNamespace UaNodeSetNs = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";

        private readonly ITestOutputHelper _output;
        private readonly List<string> _generatedFiles = new();

        public NodeSetConversionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            foreach (var f in _generatedFiles)
            {
                if (File.Exists(f)) File.Delete(f);
            }
        }

        private static IEnumerable<string> FindUaNodeSetXmlFiles()
        {
            foreach (var xmlFile in Directory.EnumerateFiles(NodeSetsDir, "*.xml", SearchOption.AllDirectories))
            {
                if (IsUaNodeSetXml(xmlFile))
                {
                    yield return xmlFile;
                }
            }
        }

        private static bool IsUaNodeSetXml(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath);
                var doc = XDocument.Load(reader);
                return doc.Root?.Name == UaNodeSetNs + "UANodeSet";
            }
            catch
            {
                return false;
            }
        }

        public static IEnumerable<object[]> UaNodeSetFiles()
        {
            foreach (var file in FindUaNodeSetXmlFiles())
            {
                var relative = Path.GetRelativePath(NodeSetsDir, file);
                yield return new object[] { relative };
            }
        }

        [Theory]
        [MemberData(nameof(UaNodeSetFiles))]
        public void XmlToJsonAndArchive_RoundTripsMatch(string relativeXmlPath)
        {
            var xmlPath = Path.Combine(NodeSetsDir, relativeXmlPath);
            var jsonPath = Path.ChangeExtension(xmlPath, ".json");
            var archivePath = xmlPath + ".tar.gz";
            _generatedFiles.Add(jsonPath);
            _generatedFiles.Add(archivePath);

            var source = new NodeSetSerializer();
            source.Load(xmlPath);

            source.SaveJson(jsonPath);
            source.SaveArchive(archivePath, 0);
            Assert.True(File.Exists(jsonPath), $"JSON file was not created: {jsonPath}");
            Assert.True(File.Exists(archivePath), $"Archive file was not created: {archivePath}");

            var fromJson = new NodeSetSerializer();
            fromJson.Load(jsonPath);
            Assert.True(source.Compare(fromJson), FormatErrors(relativeXmlPath, "JSON round-trip", source));

            var fromArchive = new NodeSetSerializer();
            fromArchive.Load(archivePath);
            Assert.True(source.Compare(fromArchive), FormatErrors(relativeXmlPath, "Archive round-trip", source));

            Assert.True(fromJson.Compare(fromArchive), FormatErrors(relativeXmlPath, "JSON vs Archive", fromJson));
        }

        private string FormatErrors(string file, string format, NodeSetSerializer serializer)
        {
            var errors = serializer.CompareErrors;
            if (errors.Count == 0) return $"{file} ({format}): No errors";
            var detail = string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
            return $"{file} ({format}):{Environment.NewLine}{detail}";
        }
    }
}
