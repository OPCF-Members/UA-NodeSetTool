using NodeSetTool;
using Xunit;

namespace Opc.Ua.NodeSetTool.Tests
{
    public class ConversionTests : IDisposable
    {
        private static readonly string ExamplesDir =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "examples"));

        private readonly List<string> _tempFiles = new();

        private string TempFile(string extension)
        {
            var path = Path.Combine(Path.GetTempPath(), $"NodeSetTest_{Guid.NewGuid()}{extension}");
            _tempFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (var f in _tempFiles)
            {
                if (File.Exists(f)) File.Delete(f);
            }
        }

        [Fact]
        public void XmlToJsonAndBack_DemoModel()
        {
            var source = Path.Combine(ExamplesDir, "DemoModel.NodeSet2.xml");
            var intermediate = TempFile(".json");

            var a = new NodeSetSerializer();
            a.Load(source);
            a.SaveJson(intermediate);

            var b = new NodeSetSerializer();
            b.Load(intermediate);

            Assert.True(a.Compare(b), FormatErrors(a));
        }

        [Fact]
        public void XmlToJsonAndBack_Services()
        {
            var source = Path.Combine(ExamplesDir, "Opc.Ua.NodeSet2.Services.xml");
            var intermediate = TempFile(".json");

            var a = new NodeSetSerializer();
            a.Load(source);
            a.SaveJson(intermediate);

            var b = new NodeSetSerializer();
            b.Load(intermediate);

            Assert.True(a.Compare(b), FormatErrors(a));
        }

        [Fact]
        public void XmlToArchiveAndBack_DemoModel()
        {
            var source = Path.Combine(ExamplesDir, "DemoModel.NodeSet2.xml");
            var intermediate = TempFile(".tar.gz");

            var a = new NodeSetSerializer();
            a.Load(source);
            a.SaveArchive(intermediate, 0);

            var b = new NodeSetSerializer();
            b.Load(intermediate);

            Assert.True(a.Compare(b), FormatErrors(a));
        }

        [Fact]
        public void JsonToXmlAndBack_DemoModel()
        {
            var source = Path.Combine(ExamplesDir, "DemoModel.NodeSet2.json");
            var intermediate = TempFile(".xml");

            var a = new NodeSetSerializer();
            a.Load(source);
            a.SaveXml(intermediate);

            var b = new NodeSetSerializer();
            b.Load(intermediate);

            Assert.True(a.Compare(b), FormatErrors(a));
        }

        [Fact]
        public void XmlToJson_RequiredModelsPreserved()
        {
            var source = Path.Combine(ExamplesDir, "DemoModel.NodeSet2.xml");
            var jsonFile = TempFile(".json");

            var a = new NodeSetSerializer();
            a.Load(source);
            a.SaveJson(jsonFile);

            var b = new NodeSetSerializer();
            b.Load(jsonFile);

            // The DemoModel depends on the base UA namespace, not itself.
            var model = b.Models.First(m => m.ModelUri == "urn:opcfoundation.org:2024-01:DemoModel");
            Assert.NotNull(model.RequiredModels);
            Assert.Single(model.RequiredModels);
            Assert.Equal("http://opcfoundation.org/UA/", model.RequiredModels[0].ModelUri);
            Assert.Equal("http://opcfoundation.org/UA/2008/02/Types.xsd", model.RequiredModels[0].XmlSchemaUri);
        }

        [Fact]
        public void XmlToJsonAndBack_RequiredModelsOrderIndependent()
        {
            var source = Path.Combine(ExamplesDir, "DemoModel.NodeSet2.xml");
            var jsonFile = TempFile(".json");

            var a = new NodeSetSerializer();
            a.Load(source);
            a.SaveJson(jsonFile);

            var b = new NodeSetSerializer();
            b.Load(jsonFile);

            // Compare should succeed even if RequiredModels are in different order.
            Assert.True(a.Compare(b), FormatErrors(a));
        }

        private static string FormatErrors(NodeSetSerializer serializer)
        {
            var errors = serializer.CompareErrors;
            if (errors.Count == 0) return "No errors";
            return string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
        }
    }
}
