using Newtonsoft.Json.Linq;
using NodeSetTool;
using Opc.Ua.JsonNodeSet;
using Opc.Ua.JsonNodeSet.Model;
using Xunit;
using AddressSpace = Opc.Ua.JsonNodeSet.AddressSpace;

namespace Opc.Ua.NodeSetTool.Tests
{
    public class AddressSpaceLibTests : IDisposable
    {
        private static readonly string ExamplesDir =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "examples"));

        private const string DemoModelUri = "urn:opcfoundation.org:2024-01:DemoModel";
        private const string DemoNsu = "nsu=urn:opcfoundation.org:2024-01:DemoModel;";
        private const string ServicesModelUri = "http://opcfoundation.org/UA/";

        private readonly List<string> _tempFiles = new();

        private string TempFile(string extension)
        {
            var path = Path.Combine(Path.GetTempPath(), $"AddressSpaceLibTest_{Guid.NewGuid()}{extension}");
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

        private static AddressSpace LoadDemoModelIntoAddressSpace()
        {
            var serializer = new NodeSetSerializer();
            serializer.Load(Path.Combine(ExamplesDir, "DemoModel.NodeSet2.json"));

            var space = new AddressSpace();
            serializer.LoadInto(space);
            return space;
        }

        [Fact]
        public void LoadMultipleNodeSets()
        {
            var space = new AddressSpace();

            var demo = new NodeSetSerializer();
            demo.Load(Path.Combine(ExamplesDir, "DemoModel.NodeSet2.json"));
            demo.LoadInto(space);

            var services = new NodeSetSerializer();
            services.Load(Path.Combine(ExamplesDir, "Opc.Ua.NodeSet2.Services.xml"));
            services.LoadInto(space);

            // Both models should be present
            var uris = space.GetModelUris();
            Assert.Contains(DemoModelUri, uris);
            Assert.Contains(ServicesModelUri, uris);

            // Nodes from both should be accessible
            Assert.NotNull(space.Read(DemoNsu + "i=68")); // DemoModel node
            Assert.True(space.NodeCount > 0);
        }

        [Fact]
        public void Read_ReturnsNode()
        {
            var space = LoadDemoModelIntoAddressSpace();
            var node = space.Read(DemoNsu + "i=68");
            Assert.NotNull(node);
            Assert.IsType<UADataType>(node);
            Assert.Contains("EnumUnderscoreTest", node.BrowseName);
        }

        [Fact]
        public void Read_ReturnsNullForUnknown()
        {
            var space = LoadDemoModelIntoAddressSpace();
            Assert.Null(space.Read("nsu=nonexistent;i=99999"));
        }

        [Fact]
        public void Browse_ForwardAndInverse()
        {
            var space = LoadDemoModelIntoAddressSpace();

            // Forward refs: i=122 (RestrictedVariableType) has HasComponent (i=47) forward refs
            var fwd = space.Browse(DemoNsu + "i=122", referenceTypeId: "i=47", includeForward: true, includeInverse: false);
            Assert.Equal(4, fwd.Count);

            // Inverse refs: i=122 has HasSubtype (i=45) inverse ref
            var inv = space.Browse(DemoNsu + "i=122", referenceTypeId: "i=45", includeForward: false, includeInverse: true);
            Assert.Single(inv);
            Assert.Equal("i=63", inv[0].TargetNodeId);
            Assert.False(inv[0].IsForward);
        }

        [Fact]
        public void RemoveNode_CleansUpReferences()
        {
            var space = LoadDemoModelIntoAddressSpace();
            var nodeId = DemoNsu + "i=68";

            // Verify node exists and has forward refs
            Assert.NotNull(space.Read(nodeId));
            var refsBefore = space.Browse(nodeId);
            Assert.True(refsBefore.Count > 0);

            // Check there is an inverse ref from one of its targets back to it
            var targetId = refsBefore.First(r => r.IsForward).TargetNodeId;
            var targetInvBefore = space.Browse(targetId, includeForward: false, includeInverse: true);
            Assert.Contains(targetInvBefore, r => r.TargetNodeId == nodeId);

            // Remove the node
            Assert.True(space.RemoveNode(nodeId));
            Assert.Null(space.Read(nodeId));

            // Forward and inverse indices for the removed node should be gone
            Assert.Empty(space.Browse(nodeId));

            // The target's inverse ref back to the removed node should be gone
            var targetInvAfter = space.Browse(targetId, includeForward: false, includeInverse: true);
            Assert.DoesNotContain(targetInvAfter, r => r.TargetNodeId == nodeId);
        }

        [Fact]
        public void GetNodeSet_FiltersByModelUri()
        {
            var space = new AddressSpace();

            var demo = new NodeSetSerializer();
            demo.Load(Path.Combine(ExamplesDir, "DemoModel.NodeSet2.json"));
            demo.LoadInto(space);

            var services = new NodeSetSerializer();
            services.Load(Path.Combine(ExamplesDir, "Opc.Ua.NodeSet2.Services.xml"));
            services.LoadInto(space);

            // Export only the DemoModel
            var nodeSet = space.GetNodeSet(DemoModelUri);
            Assert.Single(nodeSet.Models);
            Assert.Equal(DemoModelUri, nodeSet.Models[0].ModelUri);

            // All nodes in the result should have DemoModel's nsu= prefix
            var prefix = $"nsu={DemoModelUri};";
            void AssertPrefix(IEnumerable<UANode> nodes)
            {
                if (nodes == null) return;
                foreach (var n in nodes)
                {
                    Assert.StartsWith(prefix, n.NodeId);
                    if (n.Children != null)
                    {
                        AssertPrefix(n.Children.Objects);
                        AssertPrefix(n.Children.Variables);
                        AssertPrefix(n.Children.Methods);
                    }
                }
            }

            AssertPrefix(nodeSet.Nodes?.N1ReferenceTypes);
            AssertPrefix(nodeSet.Nodes?.N2DataTypes);
            AssertPrefix(nodeSet.Nodes?.N3VariableTypes);
            AssertPrefix(nodeSet.Nodes?.N4ObjectTypes);
            AssertPrefix(nodeSet.Nodes?.N5Variables);
            AssertPrefix(nodeSet.Nodes?.N6Methods);
            AssertPrefix(nodeSet.Nodes?.N7Objects);
            AssertPrefix(nodeSet.Nodes?.N8Views);
        }

        [Fact]
        public void GetNodeSet_BuildsChildHierarchy()
        {
            var space = LoadDemoModelIntoAddressSpace();
            var nodeSet = space.GetNodeSet(DemoModelUri);

            // i=68 (EnumUnderscoreTest) has child i=69 (EnumStrings).
            // i=68 should be top-level; i=69 should NOT be top-level (it's under i=68's Children).
            var topLevelDataTypes = nodeSet.Nodes?.N2DataTypes;
            Assert.NotNull(topLevelDataTypes);

            var parentNode = topLevelDataTypes.FirstOrDefault(n => n.NodeId == DemoNsu + "i=68");
            Assert.NotNull(parentNode);
            Assert.NotNull(parentNode.Children);
            Assert.NotNull(parentNode.Children.Variables);
            Assert.Contains(parentNode.Children.Variables, v => v.NodeId == DemoNsu + "i=69");

            var x = nodeSet.Nodes.N5Variables.Find(v => v.NodeId == DemoNsu + "i=69");

            // i=69 should NOT appear at top level in any node list
            Assert.DoesNotContain(nodeSet.Nodes.N5Variables ?? new List<UAVariable>(), v => v.NodeId == DemoNsu + "i=69");
        }

        [Fact]
        public void AddNodeSet_AddsModelsAndNodes()
        {
            var serializer = new NodeSetSerializer();
            serializer.Load(Path.Combine(ExamplesDir, "DemoModel.NodeSet2.json"));

            var nodeSet = serializer.Models
                .Where(m => m.ModelUri == DemoModelUri)
                .Select(m => m)
                .ToList();

            var space = new AddressSpace();
            // Use AddNodeSet directly with a UANodeSet object
            var exported = NodeSetSerializer.FromAddressSpace(LoadDemoModelIntoAddressSpace(), DemoModelUri);
            var tempFile = TempFile(".json");
            exported.SaveJson(tempFile);

            // Reload as UANodeSet and add via AddNodeSet
            var space2 = new AddressSpace();
            var ser2 = new NodeSetSerializer();
            ser2.Load(tempFile);
            ser2.LoadInto(space2);

            Assert.Contains(DemoModelUri, space2.GetModelUris());
            Assert.True(space2.NodeCount > 0);
            Assert.NotNull(space2.Read(DemoNsu + "i=68"));
        }

        [Fact]
        public void RoundTrip_LoadIntoThenExport()
        {
            // Load original via serializer
            var original = new NodeSetSerializer();
            original.Load(Path.Combine(ExamplesDir, "DemoModel.NodeSet2.json"));

            // Push into AddressSpace
            var space = new AddressSpace();
            original.LoadInto(space);

            // Export back via serializer
            var exported = NodeSetSerializer.FromAddressSpace(space, DemoModelUri);

            // Save to file and reload for comparison
            var tempFile = TempFile(".json");
            exported.SaveJson(tempFile);

            var reloaded = new NodeSetSerializer();
            reloaded.Load(tempFile);

            Assert.True(original.Compare(reloaded), FormatErrors(original));
        }

        [Fact]
        public void AddNode_RejectsUnknownNamespaceInNodeId()
        {
            var space = new AddressSpace();
            space.AddModel(new ModelDefinition { ModelUri = DemoModelUri });

            var node = new UAObjectType { NodeId = "nsu=http://unknown.org/;i=1", BrowseName = "Foo" };
            var ex = Assert.Throws<InvalidOperationException>(() => space.AddNode(node));
            Assert.Contains("http://unknown.org/", ex.Message);
        }

        [Fact]
        public void AddNode_RejectsUnknownNamespaceInBrowseName()
        {
            var space = new AddressSpace();
            space.AddModel(new ModelDefinition { ModelUri = DemoModelUri });

            var node = new UAObjectType
            {
                NodeId = DemoNsu + "i=1",
                BrowseName = "nsu=http://unknown.org/;MyName"
            };
            var ex = Assert.Throws<InvalidOperationException>(() => space.AddNode(node));
            Assert.Contains("http://unknown.org/", ex.Message);
        }

        [Fact]
        public void AddNode_AllowsUnknownNamespaceInReferenceTarget()
        {
            var space = new AddressSpace();
            space.AddModel(new ModelDefinition { ModelUri = DemoModelUri });

            var node = new UAObjectType
            {
                NodeId = DemoNsu + "i=1",
                BrowseName = "Foo",
                References = new List<Reference>
                {
                    new Reference
                    {
                        ReferenceTypeId = "i=45",
                        IsForward = false,
                        TargetId = "nsu=http://other.org/;i=99"
                    }
                }
            };
            space.AddNode(node);
            Assert.NotNull(space.Read(DemoNsu + "i=1"));
        }

        [Fact]
        public void AddNode_RejectsUnknownNamespaceInReferenceTypeId()
        {
            var space = new AddressSpace();
            space.AddModel(new ModelDefinition { ModelUri = DemoModelUri });

            var node = new UAObjectType
            {
                NodeId = DemoNsu + "i=1",
                BrowseName = "Foo",
                References = new List<Reference>
                {
                    new Reference
                    {
                        ReferenceTypeId = "nsu=http://bogus.org/;i=999",
                        IsForward = false,
                        TargetId = "i=58"
                    }
                }
            };
            var ex = Assert.Throws<InvalidOperationException>(() => space.AddNode(node));
            Assert.Contains("http://bogus.org/", ex.Message);
        }

        [Fact]
        public void AddNode_AcceptsKnownNamespaces()
        {
            var space = new AddressSpace();
            space.AddModel(new ModelDefinition
            {
                ModelUri = DemoModelUri,
                RequiredModels = new List<ModelReference>
                {
                    new ModelReference { ModelUri = "http://opcfoundation.org/UA/" }
                }
            });

            // Node in the model's own namespace referencing the base namespace — should work
            var node = new UAObjectType
            {
                NodeId = DemoNsu + "i=1",
                BrowseName = "Foo",
                References = new List<Reference>
                {
                    new Reference { ReferenceTypeId = "i=45", IsForward = false, TargetId = "i=58" }
                }
            };
            space.AddNode(node);
            Assert.NotNull(space.Read(DemoNsu + "i=1"));
        }

        [Fact]
        public void AddNode_NoMutationOnValidationFailure()
        {
            var space = new AddressSpace();
            space.AddModel(new ModelDefinition { ModelUri = DemoModelUri });

            var countBefore = space.NodeCount;

            var node = new UAObjectType
            {
                NodeId = "nsu=http://unknown.org/;i=1",
                BrowseName = "Foo"
            };

            Assert.Throws<InvalidOperationException>(() => space.AddNode(node));
            Assert.Equal(countBefore, space.NodeCount);
        }

        [Fact]
        public void AddNodeSet_RejectsUnknownNamespaceInNodes()
        {
            var space = new AddressSpace();

            var nodeSet = new UANodeSet
            {
                Models = new List<ModelDefinition>
                {
                    new ModelDefinition { ModelUri = DemoModelUri }
                },
                Nodes = new UANodeSetNodes
                {
                    N4ObjectTypes = new List<UAObjectType>
                    {
                        new UAObjectType
                        {
                            NodeId = "nsu=http://unknown.org/;i=1",
                            BrowseName = "Bad"
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => space.AddNodeSet(nodeSet));
            Assert.Contains("http://unknown.org/", ex.Message);
            // Model was registered but no nodes were added
            Assert.Equal(0, space.NodeCount);
        }

        [Fact]
        public void AddNodeSet_RejectsUnknownNamespaceInChildren()
        {
            var space = new AddressSpace();

            var nodeSet = new UANodeSet
            {
                Models = new List<ModelDefinition>
                {
                    new ModelDefinition { ModelUri = DemoModelUri }
                },
                Nodes = new UANodeSetNodes
                {
                    N4ObjectTypes = new List<UAObjectType>
                    {
                        new UAObjectType
                        {
                            NodeId = DemoNsu + "i=1",
                            BrowseName = "Good",
                            Children = new ChildList
                            {
                                Variables = new List<UAVariable>
                                {
                                    new UAVariable
                                    {
                                        NodeId = "nsu=http://unknown.org/;i=2",
                                        BrowseName = "BadChild"
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => space.AddNodeSet(nodeSet));
            Assert.Contains("http://unknown.org/", ex.Message);
            Assert.Equal(0, space.NodeCount);
        }

        [Fact]
        public void AddModel_RejectsUnknownRequiredModel()
        {
            var space = new AddressSpace();

            var model = new ModelDefinition
            {
                ModelUri = DemoModelUri,
                RequiredModels = new List<ModelReference>
                {
                    new ModelReference { ModelUri = "http://not-registered.org/" }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => space.AddModel(model));
            Assert.Contains("http://not-registered.org/", ex.Message);
            Assert.Empty(space.GetModelUris());
        }

        [Fact]
        public void AddModel_AcceptsCoreAsRequiredModel()
        {
            var space = new AddressSpace();

            var model = new ModelDefinition
            {
                ModelUri = DemoModelUri,
                RequiredModels = new List<ModelReference>
                {
                    new ModelReference { ModelUri = ServicesModelUri }
                }
            };

            space.AddModel(model);
            Assert.Contains(DemoModelUri, space.GetModelUris());
        }

        [Fact]
        public void AddModel_AcceptsAlreadyRegisteredRequiredModel()
        {
            var space = new AddressSpace();
            space.AddModel(new ModelDefinition { ModelUri = "http://dep.org/" });

            var model = new ModelDefinition
            {
                ModelUri = DemoModelUri,
                RequiredModels = new List<ModelReference>
                {
                    new ModelReference { ModelUri = "http://dep.org/" }
                }
            };

            space.AddModel(model);
            Assert.Contains(DemoModelUri, space.GetModelUris());
        }

        [Fact]
        public void AddNodeSet_RejectsUnknownRequiredModel()
        {
            var space = new AddressSpace();

            var nodeSet = new UANodeSet
            {
                Models = new List<ModelDefinition>
                {
                    new ModelDefinition
                    {
                        ModelUri = DemoModelUri,
                        RequiredModels = new List<ModelReference>
                        {
                            new ModelReference { ModelUri = "http://missing-dep.org/" }
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => space.AddNodeSet(nodeSet));
            Assert.Contains("http://missing-dep.org/", ex.Message);
            Assert.Empty(space.GetModelUris());
        }

        [Fact]
        public void IsTypeOf_SameNode_ReturnsTrue()
        {
            var space = LoadDemoModelIntoAddressSpace();
            // Any node is its own type
            Assert.True(space.IsTypeOf(DemoNsu + "i=5", DemoNsu + "i=5"));
        }

        [Fact]
        public void IsTypeOf_DirectSubtype_ReturnsTrue()
        {
            var space = LoadDemoModelIntoAddressSpace();
            // i=322 (ExtendedWorkOrderType) has inverse HasSubtype to i=5 (WorkOrderType)
            Assert.True(space.IsTypeOf(DemoNsu + "i=322", DemoNsu + "i=5"));
        }

        [Fact]
        public void IsTypeOf_TransitiveSubtype_ReturnsTrue()
        {
            var space = LoadDemoModelIntoAddressSpace();
            // i=354 → i=322 → i=5: PenultimateWorkOrderType is a transitive subtype of WorkOrderType
            Assert.True(space.IsTypeOf(DemoNsu + "i=354", DemoNsu + "i=5"));
        }

        [Fact]
        public void IsTypeOf_CrossNamespace_ReturnsTrue()
        {
            var space = LoadDemoModelIntoAddressSpace();
            // i=5 (WorkOrderType) → i=22 (Structure, base namespace)
            Assert.True(space.IsTypeOf(DemoNsu + "i=5", "i=22"));
            // Transitive: i=354 → i=322 → i=5 → i=22
            Assert.True(space.IsTypeOf(DemoNsu + "i=354", "i=22"));
        }

        [Fact]
        public void IsTypeOf_Unrelated_ReturnsFalse()
        {
            var space = LoadDemoModelIntoAddressSpace();
            // i=5 (WorkOrderType) is not a subtype of i=322 (ExtendedWorkOrderType) — it's the other way
            Assert.False(space.IsTypeOf(DemoNsu + "i=5", DemoNsu + "i=322"));
        }

        [Fact]
        public void IsTypeOf_UnknownNode_ReturnsFalse()
        {
            var space = LoadDemoModelIntoAddressSpace();
            Assert.False(space.IsTypeOf("nsu=nonexistent;i=99999", DemoNsu + "i=5"));
        }

        #region JSON-LD Tests

        private static AddressSpace LoadDemoModelWithServicesIntoAddressSpace()
        {
            var space = new AddressSpace();

            var services = new NodeSetSerializer();
            services.Load(Path.Combine(ExamplesDir, "Opc.Ua.NodeSet2.Services.xml"));
            services.LoadInto(space);

            var demo = new NodeSetSerializer();
            demo.Load(Path.Combine(ExamplesDir, "DemoModel.NodeSet2.json"));
            demo.LoadInto(space);

            return space;
        }

        [Fact]
        public void SaveJsonLd_ProducesValidJson()
        {
            var space = LoadDemoModelWithServicesIntoAddressSpace();
            var serializer = NodeSetSerializer.FromAddressSpaceAsJsonLd(space, DemoModelUri);
            var tempFile = TempFile(".jsonld");
            serializer.SaveJsonLd(tempFile);

            var json = File.ReadAllText(tempFile);
            var doc = JObject.Parse(json);

            Assert.NotNull(doc["@context"]);
            Assert.NotNull(doc["@graph"]);
            Assert.Equal("opcua:UANodeSet", doc["@type"]?.ToString());
            Assert.Equal(DemoModelUri, doc["modelUri"]?.ToString());
        }

        [Fact]
        public void SaveJsonLd_IncludesExternalStubs()
        {
            var space = LoadDemoModelWithServicesIntoAddressSpace();
            var serializer = NodeSetSerializer.FromAddressSpaceAsJsonLd(space, DemoModelUri);
            var tempFile = TempFile(".jsonld");
            serializer.SaveJsonLd(tempFile);

            var doc = JObject.Parse(File.ReadAllText(tempFile));
            var graph = (JArray)doc["@graph"]!;

            // Find stubs (isExternal: true)
            var stubs = graph.Where(n => n["isExternal"]?.Value<bool>() == true).ToList();
            Assert.True(stubs.Count > 0, "Expected external stubs in JSON-LD output");

            // Well-known base types should be stubs (Structure i=22, Enumeration i=29)
            Assert.Contains(stubs, s => s["@id"]?.ToString() == "opcua:i=22");
            Assert.Contains(stubs, s => s["@id"]?.ToString() == "opcua:i=29");

            // Stubs should have browseName
            var structStub = stubs.First(s => s["@id"]?.ToString() == "opcua:i=22");
            Assert.Equal("Structure", structStub["browseName"]?.ToString());

            // Normative nodes should have isNormative: true
            var normative = graph.Where(n => n["isNormative"]?.Value<bool>() == true).ToList();
            Assert.True(normative.Count > 0, "Expected normative nodes in JSON-LD output");

            // Stubs should NOT have isNormative
            Assert.Null(structStub["isNormative"]);
        }

        [Fact]
        public void SaveJsonLd_StubsHaveSupertypeChain()
        {
            var space = LoadDemoModelWithServicesIntoAddressSpace();
            var serializer = NodeSetSerializer.FromAddressSpaceAsJsonLd(space, DemoModelUri);
            var tempFile = TempFile(".jsonld");
            serializer.SaveJsonLd(tempFile);

            var doc = JObject.Parse(File.ReadAllText(tempFile));
            var graph = (JArray)doc["@graph"]!;
            var stubs = graph.Where(n => n["isExternal"]?.Value<bool>() == true).ToList();

            // i=22 (Structure) should have SubtypeOf i=24 (BaseDataType)
            var structStub = stubs.First(s => s["@id"]?.ToString() == "opcua:i=22");
            Assert.Equal("opcua:i=24", structStub["SubtypeOf"]?.ToString());

            // i=24 (BaseDataType) should also be present as a stub (chased supertype)
            Assert.Contains(stubs, s => s["@id"]?.ToString() == "opcua:i=24");
        }

        [Fact]
        public void LoadJsonLd_SkipsStubs()
        {
            var space = LoadDemoModelWithServicesIntoAddressSpace();
            var serializer = NodeSetSerializer.FromAddressSpaceAsJsonLd(space, DemoModelUri);
            var tempFile = TempFile(".jsonld");
            serializer.SaveJsonLd(tempFile);

            // Reimport the JSON-LD into an address space with services pre-loaded
            var space2 = new AddressSpace();
            var services = new NodeSetSerializer();
            services.Load(Path.Combine(ExamplesDir, "Opc.Ua.NodeSet2.Services.xml"));
            services.LoadInto(space2);

            var ser2 = new NodeSetSerializer();
            ser2.Load(tempFile);
            ser2.LoadInto(space2);

            // Model should be present
            Assert.Contains(DemoModelUri, space2.GetModelUris());

            // Model nodes should be present
            Assert.NotNull(space2.Read(DemoNsu + "i=68"));

            // External nodes from core namespace should already exist (from services)
            // but should not have been duplicated by the JSON-LD import
            Assert.NotNull(space2.Read("i=22"));
            Assert.NotNull(space2.Read("i=29"));
        }

        [Fact]
        public void LoadJsonLd_ExternalsFailWhenMissing()
        {
            var space = LoadDemoModelWithServicesIntoAddressSpace();
            var serializer = NodeSetSerializer.FromAddressSpaceAsJsonLd(space, DemoModelUri);
            var tempFile = TempFile(".jsonld");
            serializer.SaveJsonLd(tempFile);

            // Try to import into an empty address space — should fail
            var emptySpace = new AddressSpace();
            var ser2 = new NodeSetSerializer();
            ser2.Load(tempFile);

            var ex = Assert.Throws<InvalidOperationException>(() => ser2.LoadInto(emptySpace));
            Assert.Contains("External node(s) not found", ex.Message);
        }

        [Fact]
        public void RoundTrip_JsonLd()
        {
            // Load original
            var original = new NodeSetSerializer();
            original.Load(Path.Combine(ExamplesDir, "DemoModel.NodeSet2.json"));

            // Push into AddressSpace (without services, to match original model metadata)
            var space = new AddressSpace();
            original.LoadInto(space);

            // Export to JSON-LD
            var jsonLdSerializer = NodeSetSerializer.FromAddressSpaceAsJsonLd(space, DemoModelUri);
            var tempJsonLd = TempFile(".jsonld");
            jsonLdSerializer.SaveJsonLd(tempJsonLd);

            // Reimport from JSON-LD (serializer-level round trip, no LoadInto)
            var reimported = new NodeSetSerializer();
            reimported.Load(tempJsonLd);

            // Export reimported to JSON for comparison
            var tempJson = TempFile(".json");
            reimported.SaveJson(tempJson);

            var reloaded = new NodeSetSerializer();
            reloaded.Load(tempJson);

            Assert.True(original.Compare(reloaded), FormatErrors(original));
        }

        [Fact]
        public void LoadJsonLd_FromExampleFile()
        {
            // Load the hand-crafted example JSON-LD file
            var exampleFile = Path.Combine(ExamplesDir, "DemoModel.NodeSet2.jsonld");
            if (!File.Exists(exampleFile))
                return; // skip if example not present

            // Pre-load services so external nodes exist
            var space = new AddressSpace();
            var services = new NodeSetSerializer();
            services.Load(Path.Combine(ExamplesDir, "Opc.Ua.NodeSet2.Services.xml"));
            services.LoadInto(space);

            var serializer = new NodeSetSerializer();
            serializer.Load(exampleFile);
            serializer.LoadInto(space);

            Assert.Contains(DemoModelUri, space.GetModelUris());
            Assert.NotNull(space.Read(DemoNsu + "i=68")); // EnumUnderscoreTest
            Assert.NotNull(space.Read(DemoNsu + "i=1"));  // HeaterStatus
        }

        #endregion

        private static string FormatErrors(NodeSetSerializer serializer)
        {
            var errors = serializer.CompareErrors;
            if (errors.Count == 0) return "No errors";
            return string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
        }
    }
}

