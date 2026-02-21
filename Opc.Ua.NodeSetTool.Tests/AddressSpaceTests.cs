using NodeSetTool;
using Opc.Ua.JsonNodeSet.Model;
using Xunit;
using Variant = Opc.Ua.JsonNodeSet.Model.Variant;

namespace Opc.Ua.NodeSetTool.Tests
{
    public class AddressSpaceTests
    {
        private static readonly string ExamplesDir =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "examples"));

        private const string Nsu = "nsu=urn:opcfoundation.org:2024-01:DemoModel;";

        private static AddressSpace LoadDemoModel()
        {
            var space = new AddressSpace();
            space.LoadJson(Path.Combine(ExamplesDir, "DemoModel.NodeSet2.json"));
            return space;
        }

        [Fact]
        public void LoadJson_LoadsAllNodes()
        {
            var space = LoadDemoModel();
            Assert.True(space.NodeCount > 0, "AddressSpace should contain nodes");
        }

        [Fact]
        public void LoadJson_LoadsModels()
        {
            var space = LoadDemoModel();
            Assert.NotEmpty(space.Models);
            Assert.Equal("urn:opcfoundation.org:2024-01:DemoModel", space.Models[0].ModelUri);
        }

        [Fact]
        public void Read_ReturnsExistingNode()
        {
            var space = LoadDemoModel();
            var node = space.Read(Nsu + "i=68");
            Assert.NotNull(node);
            Assert.IsType<UADataType>(node);
            Assert.Contains("EnumUnderscoreTest", node.BrowseName);
        }

        [Fact]
        public void Read_ReturnsNullForUnknownNode()
        {
            var space = LoadDemoModel();
            Assert.Null(space.Read("nsu=nonexistent;i=99999"));
        }

        [Fact]
        public void Read_LoadsChildNodes()
        {
            var space = LoadDemoModel();
            var child = space.Read(Nsu + "i=69");
            Assert.NotNull(child);
            Assert.IsType<UAVariable>(child);
            Assert.Equal(Nsu + "i=68", child.ParentId);
        }

        [Fact]
        public void Read_LoadsNestedChildren()
        {
            var space = LoadDemoModel();
            // i=57 is a depth-2 child variable under i=125 which is under i=124
            var node = space.Read(Nsu + "i=57");
            Assert.NotNull(node);
            Assert.Equal(Nsu + "i=125", node.ParentId);
        }

        [Fact]
        public void Browse_ReturnsForwardReferences()
        {
            var space = LoadDemoModel();
            // i=122 (RestrictedVariableType) has 4 forward HasComponent (i=47) refs
            var refs = space.Browse(Nsu + "i=122", referenceTypeId: "i=47", includeForward: true, includeInverse: false);
            Assert.Equal(4, refs.Count);
            var targetIds = refs.Select(r => r.TargetNodeId).OrderBy(t => t).ToList();
            Assert.Contains(Nsu + "i=123", targetIds);
            Assert.Contains(Nsu + "i=210", targetIds);
            Assert.Contains(Nsu + "i=211", targetIds);
            Assert.Contains(Nsu + "i=212", targetIds);
        }

        [Fact]
        public void Browse_ReturnsInverseReferences()
        {
            var space = LoadDemoModel();
            // i=122 has 1 explicit inverse HasSubtype (i=45) ref to i=63
            var refs = space.Browse(Nsu + "i=122", referenceTypeId: "i=45", includeForward: false, includeInverse: true);
            Assert.Single(refs);
            Assert.Equal("i=63", refs[0].TargetNodeId);
            Assert.False(refs[0].IsForward);
        }

        [Fact]
        public void Browse_BidirectionalIndex()
        {
            var space = LoadDemoModel();
            // i=68 has a forward ref (i=46) to i=69. From i=69's perspective, the inverse index should have that entry.
            var inverseRefs = space.Browse(Nsu + "i=69", referenceTypeId: "i=46", includeForward: false, includeInverse: true);
            Assert.Contains(inverseRefs, r => r.TargetNodeId == Nsu + "i=68" && !r.IsForward);
        }

        [Fact]
        public void Browse_NoFilter()
        {
            var space = LoadDemoModel();
            // i=122 has 4 forward HasComponent + 1 inverse HasSubtype + bidirectional entries from children
            var refs = space.Browse(Nsu + "i=122");
            Assert.True(refs.Count >= 5, $"Expected at least 5 refs, got {refs.Count}");
        }

        [Fact]
        public void Write_UpdatesVariableValue()
        {
            var space = LoadDemoModel();
            var nodeId = Nsu + "i=123"; // UAVariable (Yellow)
            var newValue = new Opc.Ua.JsonNodeSet.Model.Variant(6, 42);
            Assert.True(space.Write(nodeId, newValue));
            var node = space.Read(nodeId) as UAVariable;
            Assert.NotNull(node);
            Assert.Equal(42, node.Value.Value);
        }

        [Fact]
        public void Write_FailsForNonVariable()
        {
            var space = LoadDemoModel();
            // i=124 is a UAObjectType
            Assert.False(space.Write(Nsu + "i=124", new Opc.Ua.JsonNodeSet.Model.Variant(6, 0)));
        }

        [Fact]
        public void Write_FailsForUnknownNode()
        {
            var space = LoadDemoModel();
            Assert.False(space.Write("nsu=nonexistent;i=99999", new Opc.Ua.JsonNodeSet.Model.Variant(6, 0)));
        }

        [Fact]
        public void Subscribe_ReceivesNotificationOnWrite()
        {
            var space = LoadDemoModel();
            var nodeId = Nsu + "i=123";
            int callCount = 0;
            UANode received = null;

            space.Subscribe(nodeId, n => { callCount++; received = n; });
            space.Write(nodeId, new Opc.Ua.JsonNodeSet.Model.Variant(6, 100));

            Assert.Equal(1, callCount);
            Assert.NotNull(received);
            Assert.Equal(nodeId, received.NodeId);
        }

        [Fact]
        public void Subscribe_NoNotificationAfterDispose()
        {
            var space = LoadDemoModel();
            var nodeId = Nsu + "i=123";
            int callCount = 0;

            var sub = space.Subscribe(nodeId, _ => callCount++);
            sub.Dispose();
            space.Write(nodeId, new Opc.Ua.JsonNodeSet.Model.Variant(6, 200));

            Assert.Equal(0, callCount);
        }

        [Fact]
        public void Subscribe_MultipleSubscribers()
        {
            var space = LoadDemoModel();
            var nodeId = Nsu + "i=123";
            int count1 = 0, count2 = 0;

            space.Subscribe(nodeId, _ => count1++);
            space.Subscribe(nodeId, _ => count2++);
            space.Write(nodeId, new Opc.Ua.JsonNodeSet.Model.Variant(6, 300));

            Assert.Equal(1, count1);
            Assert.Equal(1, count2);
        }

        [Fact]
        public void IncrementalParsing_MatchesFullParse()
        {
            // Full parse via EventDrivenParser
            var filePath = Path.Combine(ExamplesDir, "DemoModel.NodeSet2.json");
            var fullNodeSet = EventDrivenParser.ParseJson<UANodeSet>(filePath);
            int fullCount = CountNodesRecursive(fullNodeSet);

            // Incremental parse via AddressSpace
            var space = LoadDemoModel();

            Assert.Equal(fullCount, space.NodeCount);
        }

        private static int CountNodesRecursive(UANodeSet nodeSet)
        {
            int count = 0;
            if (nodeSet.Nodes == null) return 0;

            void CountList(IEnumerable<UANode> nodes)
            {
                if (nodes == null) return;
                foreach (var node in nodes)
                {
                    count++;
                    if (node.Children != null)
                    {
                        CountList(node.Children.Variables);
                        CountList(node.Children.Objects);
                        CountList(node.Children.Methods);
                    }
                }
            }

            CountList(nodeSet.Nodes.N1ReferenceTypes);
            CountList(nodeSet.Nodes.N2DataTypes);
            CountList(nodeSet.Nodes.N3VariableTypes);
            CountList(nodeSet.Nodes.N4ObjectTypes);
            CountList(nodeSet.Nodes.N5Variables);
            CountList(nodeSet.Nodes.N6Methods);
            CountList(nodeSet.Nodes.N7Objects);
            CountList(nodeSet.Nodes.N8Views);

            return count;
        }
    }
}
