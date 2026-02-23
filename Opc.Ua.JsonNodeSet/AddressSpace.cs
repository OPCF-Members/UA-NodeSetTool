using Opc.Ua.JsonNodeSet.Model;

namespace Opc.Ua.JsonNodeSet
{
    public class ReferenceEntry
    {
        public string SourceNodeId { get; set; } = null!;
        public string ReferenceTypeId { get; set; } = null!;
        public string TargetNodeId { get; set; } = null!;
        public bool IsForward { get; set; }
    }

    public class AddressSpace
    {
        private const string NsuPrefix = "nsu=";
        private const string CoreNamespaceUri = "http://opcfoundation.org/UA/";
        private const string HasSubtypeId = "i=45";

        private readonly Dictionary<string, UANode> _nodes = new();
        private readonly Dictionary<string, List<ReferenceEntry>> _forwardRefs = new();
        private readonly Dictionary<string, List<ReferenceEntry>> _inverseRefs = new();
        private readonly Dictionary<string, ModelDefinition> _models = new();
        private readonly List<UANode> _sequence = new();
        private Dictionary<string, string?>? _supertypeCache;

        public int NodeCount => _nodes.Count;
        public IReadOnlyDictionary<string, ModelDefinition> Models => _models;
        public IReadOnlyList<UANode> Nodes => _sequence;

        public void AddModel(ModelDefinition model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (model.ModelUri == null)
                throw new ArgumentException("ModelDefinition.ModelUri must not be null.", nameof(model));

            // Stage 1: Validate that all RequiredModels are already registered or are the core namespace
            if (model.RequiredModels != null)
            {
                var missing = new List<string>();
                foreach (var req in model.RequiredModels)
                {
                    if (req.ModelUri == null) continue;
                    if (req.ModelUri == CoreNamespaceUri) continue;
                    if (_models.ContainsKey(req.ModelUri)) continue;
                    missing.Add(req.ModelUri);
                }
                if (missing.Count > 0)
                    throw new InvalidOperationException(
                        $"Model '{model.ModelUri}' requires unknown model(s): {string.Join(", ", missing)}");
            }

            _models[model.ModelUri] = model;
        }

        public void AddNode(UANode node, string? parentNodeId = null)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (node.NodeId == null)
                throw new ArgumentException("UANode.NodeId must not be null.", nameof(node));

            // Validate namespaces before mutating state
            var errors = new List<string>();
            var knownUris = GetKnownNamespaceUris();
            ValidateNode(node, knownUris, errors);
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    $"Unknown namespace(s) referenced: {string.Join("; ", errors)}");

            AddNodeInternal(node, parentNodeId);
        }

        public bool RemoveNode(string nodeId)
        {
            if (!_nodes.TryGetValue(nodeId, out var node))
                return false;

            // Recursively remove children first
            if (node.Children != null)
            {
                RemoveChildren(node.Children);
            }

            _nodes.Remove(nodeId);
            _sequence.Remove(node);
            InvalidateSupertypeCache();

            // Clean up forward references from this node
            if (_forwardRefs.TryGetValue(nodeId, out var fwdList))
            {
                foreach (var entry in fwdList)
                {
                    if (_inverseRefs.TryGetValue(entry.TargetNodeId, out var targetInv))
                    {
                        targetInv.RemoveAll(e => e.TargetNodeId == nodeId && e.ReferenceTypeId == entry.ReferenceTypeId);
                        if (targetInv.Count == 0) _inverseRefs.Remove(entry.TargetNodeId);
                    }
                }
                _forwardRefs.Remove(nodeId);
            }

            // Clean up inverse references from this node
            if (_inverseRefs.TryGetValue(nodeId, out var invList))
            {
                foreach (var entry in invList)
                {
                    if (_forwardRefs.TryGetValue(entry.TargetNodeId, out var targetFwd))
                    {
                        targetFwd.RemoveAll(e => e.TargetNodeId == nodeId && e.ReferenceTypeId == entry.ReferenceTypeId);
                        if (targetFwd.Count == 0) _forwardRefs.Remove(entry.TargetNodeId);
                    }
                }
                _inverseRefs.Remove(nodeId);
            }

            return true;
        }

        public UANode? Read(string nodeId)
        {
            _nodes.TryGetValue(nodeId, out var node);
            return node;
        }

        public List<ReferenceEntry> Browse(string nodeId,
            string? referenceTypeId = null,
            bool includeForward = true,
            bool includeInverse = true)
        {
            var results = new List<ReferenceEntry>();

            if (includeForward && _forwardRefs.TryGetValue(nodeId, out var fwd))
            {
                foreach (var entry in fwd)
                {
                    if (referenceTypeId == null || entry.ReferenceTypeId == referenceTypeId)
                        results.Add(entry);
                }
            }

            if (includeInverse && _inverseRefs.TryGetValue(nodeId, out var inv))
            {
                foreach (var entry in inv)
                {
                    if (referenceTypeId == null || entry.ReferenceTypeId == referenceTypeId)
                        results.Add(entry);
                }
            }

            return results;
        }

        public List<ReferenceEntry> BrowseWithSubtypes(string nodeId,
            string? referenceTypeId = null,
            bool includeForward = true,
            bool includeInverse = true)
        {
            var results = new List<ReferenceEntry>();

            if (includeForward && _forwardRefs.TryGetValue(nodeId, out var fwd))
            {
                foreach (var entry in fwd)
                {
                    if (referenceTypeId == null || IsTypeOf(entry.ReferenceTypeId, referenceTypeId))
                        results.Add(entry);
                }
            }

            if (includeInverse && _inverseRefs.TryGetValue(nodeId, out var inv))
            {
                foreach (var entry in inv)
                {
                    if (referenceTypeId == null || IsTypeOf(entry.ReferenceTypeId, referenceTypeId))
                        results.Add(entry);
                }
            }

            return results;
        }

        public bool IsTypeOf(string nodeId, string targetTypeId)
        {
            ArgumentNullException.ThrowIfNull(nodeId);
            ArgumentNullException.ThrowIfNull(targetTypeId);

            EnsureSupertypeCache();

            var current = nodeId;
            while (current != null)
            {
                if (current == targetTypeId)
                    return true;

                _supertypeCache!.TryGetValue(current, out current);
            }

            return false;
        }

        public (UANodeSet NodeSet, List<UANode> Stubs) GetNodeSetWithStubs(string modelUri)
        {
            var nodeSet = GetNodeSet(modelUri);
            var stubs = new List<UANode>();
            var stubIds = new HashSet<string>();

            // Collect all model node IDs (top-level + children)
            var modelNodeIds = new HashSet<string>();
            void CollectIds(IEnumerable<UANode>? nodes)
            {
                if (nodes == null) return;
                foreach (var n in nodes)
                {
                    if (n.NodeId != null) modelNodeIds.Add(n.NodeId);
                    CollectChildNodeIds(n, modelNodeIds);
                }
            }
            CollectIds(nodeSet.Nodes?.N1ReferenceTypes);
            CollectIds(nodeSet.Nodes?.N2DataTypes);
            CollectIds(nodeSet.Nodes?.N3VariableTypes);
            CollectIds(nodeSet.Nodes?.N4ObjectTypes);
            CollectIds(nodeSet.Nodes?.N5Variables);
            CollectIds(nodeSet.Nodes?.N6Methods);
            CollectIds(nodeSet.Nodes?.N7Objects);
            CollectIds(nodeSet.Nodes?.N8Views);

            // Collect all external NodeId references
            var externalIds = new HashSet<string>();
            void WalkNodes(IEnumerable<UANode>? nodes)
            {
                if (nodes == null) return;
                foreach (var n in nodes)
                    CollectExternalRefsFromNode(n, modelNodeIds, externalIds);
            }
            WalkNodes(nodeSet.Nodes?.N1ReferenceTypes);
            WalkNodes(nodeSet.Nodes?.N2DataTypes);
            WalkNodes(nodeSet.Nodes?.N3VariableTypes);
            WalkNodes(nodeSet.Nodes?.N4ObjectTypes);
            WalkNodes(nodeSet.Nodes?.N5Variables);
            WalkNodes(nodeSet.Nodes?.N6Methods);
            WalkNodes(nodeSet.Nodes?.N7Objects);
            WalkNodes(nodeSet.Nodes?.N8Views);

            // Create stubs and chase supertypes via BFS
            var toProcess = new Queue<string>(externalIds);
            while (toProcess.Count > 0)
            {
                var id = toProcess.Dequeue();
                if (stubIds.Contains(id) || modelNodeIds.Contains(id)) continue;

                var stub = CreateStubNode(id);
                stubs.Add(stub);
                stubIds.Add(id);

                // Chase supertype: if stub has an inverse HasSubtype, enqueue the target
                if (stub.References != null)
                {
                    foreach (var r in stub.References)
                    {
                        if (r.ReferenceTypeId == HasSubtypeId && !(r.IsForward ?? true))
                        {
                            if (r.TargetId != null && !stubIds.Contains(r.TargetId) && !modelNodeIds.Contains(r.TargetId))
                                toProcess.Enqueue(r.TargetId);
                        }
                    }
                }
            }

            return (nodeSet, stubs);
        }

        private static void CollectChildNodeIds(UANode node, HashSet<string> ids)
        {
            if (node.Children == null) return;
            void Collect(IEnumerable<UANode>? children)
            {
                if (children == null) return;
                foreach (var c in children)
                {
                    if (c.NodeId != null) ids.Add(c.NodeId);
                    CollectChildNodeIds(c, ids);
                }
            }
            Collect(node.Children.Objects);
            Collect(node.Children.Variables);
            Collect(node.Children.Methods);
        }

        private static void CollectExternalRefsFromNode(UANode node, HashSet<string> modelNodeIds, HashSet<string> externalIds)
        {
            void AddIfExternal(string? nodeId)
            {
                if (nodeId != null && !modelNodeIds.Contains(nodeId))
                    externalIds.Add(nodeId);
            }

            AddIfExternal(node.TypeId);
            AddIfExternal(node.ModellingRuleId);

            if (node is UAVariable v) AddIfExternal(v.DataType);
            if (node is UAVariableType vt) AddIfExternal(vt.DataType);

            if (node.References != null)
                foreach (var r in node.References)
                    AddIfExternal(r.TargetId);

            if (node.RolePermissions != null)
                foreach (var rp in node.RolePermissions)
                    AddIfExternal(rp.RoleId);

            if (node is UADataType dt && dt.Definition?.Fields != null)
                foreach (var f in dt.Definition.Fields)
                    AddIfExternal(f.DataType);

            if (node.Children != null)
            {
                void WalkChildren(IEnumerable<UANode>? children)
                {
                    if (children == null) return;
                    foreach (var c in children)
                        CollectExternalRefsFromNode(c, modelNodeIds, externalIds);
                }
                WalkChildren(node.Children.Objects);
                WalkChildren(node.Children.Variables);
                WalkChildren(node.Children.Methods);
            }
        }

        private UANode CreateStubNode(string nodeId)
        {
            if (_nodes.TryGetValue(nodeId, out var existing))
            {
                UANode stub = existing switch
                {
                    UADataType => new UADataType { NodeClass = NodeClass.UADataType },
                    UAObjectType => new UAObjectType { NodeClass = NodeClass.UAObjectType },
                    UAVariableType => new UAVariableType { NodeClass = NodeClass.UAVariableType },
                    UAReferenceType => new UAReferenceType { NodeClass = NodeClass.UAReferenceType },
                    UAVariable => new UAVariable { NodeClass = NodeClass.UAVariable },
                    UAMethod => new UAMethod { NodeClass = NodeClass.UAMethod },
                    UAView => new UAView { NodeClass = NodeClass.UAView },
                    _ => new UAObject { NodeClass = NodeClass.UAObject }
                };

                stub.NodeId = nodeId;
                stub.BrowseName = existing.BrowseName;

                // Include supertype reference if this node has one
                if (_inverseRefs.TryGetValue(nodeId, out var invRefs))
                {
                    foreach (var entry in invRefs)
                    {
                        if (entry.ReferenceTypeId == HasSubtypeId)
                        {
                            stub.References = new List<Reference>
                            {
                                new Reference
                                {
                                    ReferenceTypeId = HasSubtypeId,
                                    IsForward = false,
                                    TargetId = entry.TargetNodeId
                                }
                            };
                            break;
                        }
                    }
                }

                return stub;
            }
            else
            {
                // Unknown node — emit minimal stub
                return new UAObject
                {
                    NodeId = nodeId,
                    NodeClass = NodeClass.UAObject,
                    BrowseName = nodeId
                };
            }
        }

        public UANodeSet GetNodeSet(string modelUri)
        {
            var nodeSet = new UANodeSet();

            if (_models.TryGetValue(modelUri, out var model))
            {
                // Enrich RequiredModels with version/publication info from registered models
                if (model.RequiredModels != null)
                {
                    foreach (var req in model.RequiredModels)
                    {
                        if (req.ModelUri != null && _models.TryGetValue(req.ModelUri, out var registeredModel))
                        {
                            req.VarVersion ??= registeredModel.VarVersion;
                            req.PublicationDate ??= registeredModel.PublicationDate;
                        }
                    }
                }

                nodeSet.Models = new List<ModelDefinition> { model };
            }
            else
            {
                nodeSet.Models = new List<ModelDefinition>();
            }

            var prefix = $"nsu={modelUri};";
            var isCoreNamespace = string.Equals(modelUri, CoreNamespaceUri, StringComparison.OrdinalIgnoreCase);
            var filtered = new List<UANode>();
            var filteredIds = new HashSet<string>();

            foreach (var node in _sequence)
            {
                if (node.NodeId == null) continue;

                // exclude nodes with parents.
                if (node.ParentId != null)
                {
                    continue;
                }

                // Core namespace nodes may be stored without nsu= prefix (e.g. "i=123")
                if (node.NodeId.StartsWith(prefix)
                    || (isCoreNamespace && !node.NodeId.StartsWith(NsuPrefix)))
                {
                    filtered.Add(node);
                    filteredIds.Add(node.NodeId);
                }
            }

            nodeSet.Nodes = new UANodeSetNodes();

            foreach (var node in filtered)
            {
                switch (node)
                {
                    case UAReferenceType rt: (nodeSet.Nodes.N1ReferenceTypes ??= new()).Add(rt); break;
                    case UADataType dt: (nodeSet.Nodes.N2DataTypes ??= new()).Add(dt); break;
                    case UAVariableType vt: (nodeSet.Nodes.N3VariableTypes ??= new()).Add(vt); break;
                    case UAObjectType ot: (nodeSet.Nodes.N4ObjectTypes ??= new()).Add(ot); break;
                    case UAVariable vn: (nodeSet.Nodes.N5Variables ??= new()).Add(vn); break;
                    case UAMethod mn: (nodeSet.Nodes.N6Methods ??= new()).Add(mn); break;
                    case UAObject on: (nodeSet.Nodes.N7Objects ??= new()).Add(on); break;
                    case UAView wn: (nodeSet.Nodes.N8Views ??= new()).Add(wn); break;
                }
            }

            return nodeSet;
        }

        public void AddNodeSet(UANodeSet nodeSet)
        {
            ArgumentNullException.ThrowIfNull(nodeSet);

            // Stage 1: Validate and register models
            if (nodeSet.Models != null)
            {
                // Build the set of URIs that will be known after this batch:
                // existing models + all models in this nodeset + core namespace
                var batchUris = new HashSet<string> { CoreNamespaceUri };
                foreach (var kvp in _models)
                    batchUris.Add(kvp.Key);
                foreach (var model in nodeSet.Models)
                    if (model?.ModelUri != null)
                        batchUris.Add(model.ModelUri);

                // Validate that every RequiredModel URI is in the batch set
                var missing = new List<string>();
                foreach (var model in nodeSet.Models)
                {
                    if (model?.RequiredModels == null) continue;
                    foreach (var req in model.RequiredModels)
                    {
                        if (req.ModelUri != null && !batchUris.Contains(req.ModelUri))
                            missing.Add($"Model '{model.ModelUri}' requires unknown model '{req.ModelUri}'");
                    }
                }
                if (missing.Count > 0)
                    throw new InvalidOperationException(string.Join("; ", missing));

                // All valid — register them
                foreach (var model in nodeSet.Models)
                    if (model?.ModelUri != null)
                        _models[model.ModelUri] = model;
            }

            // Validate all nodes before mutating node state
            if (nodeSet.Nodes != null)
            {
                var errors = new List<string>();
                var knownUris = GetKnownNamespaceUris();

                void ValidateList(IEnumerable<UANode>? nodes)
                {
                    if (nodes == null) return;
                    foreach (var node in nodes)
                        ValidateNode(node, knownUris, errors);
                }

                ValidateList(nodeSet.Nodes.N1ReferenceTypes);
                ValidateList(nodeSet.Nodes.N2DataTypes);
                ValidateList(nodeSet.Nodes.N3VariableTypes);
                ValidateList(nodeSet.Nodes.N4ObjectTypes);
                ValidateList(nodeSet.Nodes.N5Variables);
                ValidateList(nodeSet.Nodes.N6Methods);
                ValidateList(nodeSet.Nodes.N7Objects);
                ValidateList(nodeSet.Nodes.N8Views);

                if (errors.Count > 0)
                    throw new InvalidOperationException(
                        $"Unknown namespace(s) referenced: {string.Join("; ", errors)}");

                void AddTopLevel(IEnumerable<UANode>? nodes)
                {
                    if (nodes == null) return;
                    foreach (var node in nodes)
                        AddNodeInternal(node, null);
                }

                AddTopLevel(nodeSet.Nodes.N1ReferenceTypes);
                AddTopLevel(nodeSet.Nodes.N2DataTypes);
                AddTopLevel(nodeSet.Nodes.N3VariableTypes);
                AddTopLevel(nodeSet.Nodes.N4ObjectTypes);
                AddTopLevel(nodeSet.Nodes.N5Variables);
                AddTopLevel(nodeSet.Nodes.N6Methods);
                AddTopLevel(nodeSet.Nodes.N7Objects);
                AddTopLevel(nodeSet.Nodes.N8Views);
            }
        }

        public List<string> GetModelUris()
        {
            return new List<string>(_models.Keys);
        }

        public bool RemoveModel(string modelUri)
        {
            if (!_models.ContainsKey(modelUri))
                return false;

            // Collect all node IDs belonging to this model
            var prefix = $"nsu={modelUri};";
            var nodeIds = new HashSet<string>(
                _nodes.Keys.Where(id => id.StartsWith(prefix)));

            // Check if any nodes from OTHER models depend on nodes in this model,
            // either via explicit forward references or namespace fields (TypeId, DataType, etc.).
            // Collect the set of dependent namespace URIs.
            var dependentNamespaces = new HashSet<string>();
            foreach (var node in _sequence)
            {
                if (node.NodeId != null && nodeIds.Contains(node.NodeId))
                    continue; // skip the model's own nodes

                var nodeNs = ExtractNsu(node.NodeId ?? "") ?? "";

                // Check explicit forward references targeting nodes in this model
                if (node.References != null)
                {
                    foreach (var r in node.References)
                    {
                        if (r.TargetId == null) continue;
                        bool isForward = r.IsForward ?? true;
                        if (isForward && nodeIds.Contains(r.TargetId))
                            dependentNamespaces.Add(nodeNs);
                    }
                }

                // Check namespace fields (TypeId, DataType, BrowseName, etc.)
                CollectDependentNamespace(node.NodeId, modelUri, nodeNs, dependentNamespaces);
                CollectDependentNamespace(node.BrowseName, modelUri, nodeNs, dependentNamespaces);
                CollectDependentNamespace(node.ParentId, modelUri, nodeNs, dependentNamespaces);
                CollectDependentNamespace(node.TypeId, modelUri, nodeNs, dependentNamespaces);
                CollectDependentNamespace(node.ModellingRuleId, modelUri, nodeNs, dependentNamespaces);

                if (node is UAVariable v)
                    CollectDependentNamespace(v.DataType, modelUri, nodeNs, dependentNamespaces);
                if (node is UAVariableType vt)
                    CollectDependentNamespace(vt.DataType, modelUri, nodeNs, dependentNamespaces);

                if (node.References != null)
                    foreach (var r in node.References)
                        CollectDependentNamespace(r.ReferenceTypeId, modelUri, nodeNs, dependentNamespaces);

                if (node.Children != null)
                    CollectChildrenDependencies(node.Children, modelUri, nodeIds, dependentNamespaces);
            }

            if (dependentNamespaces.Count > 0)
                throw new InvalidOperationException(
                    $"Cannot remove model '{modelUri}'\nIt is used by\n{string.Join("\n", dependentNamespaces)}");

            // Remove all nodes belonging to this model
            foreach (var nodeId in nodeIds)
            {
                RemoveNode(nodeId);
            }

            _models.Remove(modelUri);
            return true;
        }

        /// <summary>
        /// Pre-computes DataTypeForm for all UADataType nodes based on inheritance.
        /// Rules (checked in order):
        ///   1. Inherits from Union → "Union"
        ///   2. Inherits from Structure → "Structure"
        ///   3. Inherits from Enumeration → "Enumeration"
        ///   4. Inherits from UInteger AND has Fields → "OptionSet"
        ///   5. Otherwise → null (not a structured form)
        /// </summary>
        public void ComputeDataTypeForms()
        {
            var structureId = FindWellKnownNode("i=22");
            var enumerationId = FindWellKnownNode("i=29");
            var unionId = FindWellKnownNode("i=12756");
            var uintegerId = FindWellKnownNode("i=28");

            foreach (var node in _sequence)
            {
                if (node is not UADataType dt) continue;
                if (node.NodeId == null) continue;

                string? form = null;

                if (unionId != null && IsTypeOf(node.NodeId, unionId))
                    form = "Union";
                else if (structureId != null && IsTypeOf(node.NodeId, structureId))
                    form = "Structure";
                else if (enumerationId != null && IsTypeOf(node.NodeId, enumerationId))
                    form = "Enumeration";
                else if (uintegerId != null && IsTypeOf(node.NodeId, uintegerId) &&
                         dt.Definition?.Fields != null && dt.Definition.Fields.Count > 0)
                    form = "OptionSet";

                dt.DataTypeForm = form;
            }
        }

        private string? FindWellKnownNode(string shortId)
        {
            var nsuId = $"nsu={CoreNamespaceUri};{shortId}";
            if (_nodes.ContainsKey(nsuId)) return nsuId;
            if (_nodes.ContainsKey(shortId)) return shortId;
            return null;
        }

        #region Namespace Validation

        private HashSet<string> GetKnownNamespaceUris()
        {
            var known = new HashSet<string> { CoreNamespaceUri };

            foreach (var kvp in _models)
            {
                known.Add(kvp.Key);

                if (kvp.Value.RequiredModels != null)
                {
                    foreach (var req in kvp.Value.RequiredModels)
                    {
                        if (req.ModelUri != null)
                            known.Add(req.ModelUri);
                    }
                }
            }

            return known;
        }

        private static void ValidateNode(UANode node, HashSet<string> knownUris, List<string> errors)
        {
            ValidateNsuField(node.NodeId, "NodeId", node, knownUris, errors);
            ValidateNsuField(node.BrowseName, "BrowseName", node, knownUris, errors);
            ValidateNsuField(node.ParentId, "ParentId", node, knownUris, errors);
            ValidateNsuField(node.TypeId, "TypeId", node, knownUris, errors);
            ValidateNsuField(node.ModellingRuleId, "ModellingRuleId", node, knownUris, errors);

            if (node is UAVariable v)
                ValidateNsuField(v.DataType, "DataType", node, knownUris, errors);
            if (node is UAVariableType vt)
                ValidateNsuField(vt.DataType, "DataType", node, knownUris, errors);

            if (node.References != null)
            {
                foreach (var r in node.References)
                {
                    ValidateNsuField(r.ReferenceTypeId, "Reference.ReferenceTypeId", node, knownUris, errors);
                    // Reference.TargetId is intentionally not validated — references to
                    // nodes in unknown/not-yet-loaded namespaces are allowed.
                }
            }

            if (node.Children != null)
            {
                ValidateChildren(node.Children, knownUris, errors);
            }
        }

        private static void ValidateChildren(ChildList children, HashSet<string> knownUris, List<string> errors)
        {
            if (children.Objects != null)
                foreach (var child in children.Objects)
                    ValidateNode(child, knownUris, errors);
            if (children.Variables != null)
                foreach (var child in children.Variables)
                    ValidateNode(child, knownUris, errors);
            if (children.Methods != null)
                foreach (var child in children.Methods)
                    ValidateNode(child, knownUris, errors);
        }

        private static void ValidateNsuField(string? value, string fieldName, UANode node, HashSet<string> knownUris, List<string> errors)
        {
            if (value == null) return;

            var uri = ExtractNsu(value);
            if (uri != null && !knownUris.Contains(uri))
            {
                errors.Add($"Node '{node.NodeId}' {fieldName} references unknown namespace '{uri}'");
            }
        }

        private static string? ExtractNsu(string value)
        {
            if (!value.StartsWith(NsuPrefix, StringComparison.Ordinal))
                return null;

            int semi = value.IndexOf(';', NsuPrefix.Length);
            if (semi < 0)
                return null;

            return value.Substring(NsuPrefix.Length, semi - NsuPrefix.Length);
        }

        private static void CollectDependentNamespace(string? value, string targetUri, string nodeNs, HashSet<string> result)
        {
            if (value == null) return;
            var uri = ExtractNsu(value);
            if (uri != null && uri == targetUri)
                result.Add(nodeNs);
        }

        private static void CollectChildrenDependencies(ChildList children, string modelUri, HashSet<string> nodeIds, HashSet<string> result)
        {
            void Check(UANode node)
            {
                var nodeNs = ExtractNsu(node.NodeId ?? "") ?? "";

                if (node.References != null)
                {
                    foreach (var r in node.References)
                    {
                        if (r.TargetId == null) continue;
                        bool isForward = r.IsForward ?? true;
                        if (isForward && nodeIds.Contains(r.TargetId))
                            result.Add(nodeNs);
                    }
                }

                CollectDependentNamespace(node.NodeId, modelUri, nodeNs, result);
                CollectDependentNamespace(node.BrowseName, modelUri, nodeNs, result);
                CollectDependentNamespace(node.ParentId, modelUri, nodeNs, result);
                CollectDependentNamespace(node.TypeId, modelUri, nodeNs, result);
                CollectDependentNamespace(node.ModellingRuleId, modelUri, nodeNs, result);

                if (node is UAVariable v)
                    CollectDependentNamespace(v.DataType, modelUri, nodeNs, result);
                if (node is UAVariableType vt)
                    CollectDependentNamespace(vt.DataType, modelUri, nodeNs, result);

                if (node.References != null)
                    foreach (var r in node.References)
                        CollectDependentNamespace(r.ReferenceTypeId, modelUri, nodeNs, result);

                if (node.Children != null)
                    CollectChildrenDependencies(node.Children, modelUri, nodeIds, result);
            }

            if (children.Objects != null)
                foreach (var child in children.Objects) Check(child);
            if (children.Variables != null)
                foreach (var child in children.Variables) Check(child);
            if (children.Methods != null)
                foreach (var child in children.Methods) Check(child);
        }


        #endregion

        #region Supertype Cache

        private void InvalidateSupertypeCache()
        {
            _supertypeCache = null;
        }

        private void EnsureSupertypeCache()
        {
            if (_supertypeCache != null) return;

            _supertypeCache = new Dictionary<string, string?>();

            foreach (var nodeId in _nodes.Keys)
            {
                if (_supertypeCache.ContainsKey(nodeId)) continue;

                // The supertype is the target of an inverse HasSubtype reference from this node.
                // In the reference model: node has inverse HasSubtype → target means node is subtype of target.
                string? supertype = null;

                if (_inverseRefs.TryGetValue(nodeId, out var invRefs))
                {
                    foreach (var entry in invRefs)
                    {
                        if (entry.ReferenceTypeId == HasSubtypeId)
                        {
                            supertype = entry.TargetNodeId;
                            break;
                        }
                    }
                }

                _supertypeCache[nodeId] = supertype;
            }
        }

        #endregion

        #region Internal Mutation

        private void AddNodeInternal(UANode node, string? parentNodeId)
        {
            if (node?.NodeId == null)
                throw new ArgumentException("UANode and UANode.NodeId must not be null.");

            if (parentNodeId != null && node.ParentId == null)
            {
                node.ParentId = parentNodeId;
            }

            _nodes[node.NodeId] = node;
            _sequence.Add(node);
            InvalidateSupertypeCache();

            if (node.References != null)
            {
                foreach (var r in node.References)
                {
                    if (r.TargetId == null || r.ReferenceTypeId == null) continue;

                    bool isForward = r.IsForward ?? true;

                    var entry = new ReferenceEntry
                    {
                        SourceNodeId = node.NodeId,
                        ReferenceTypeId = r.ReferenceTypeId,
                        TargetNodeId = r.TargetId,
                        IsForward = isForward
                    };

                    if (isForward)
                    {
                        AddIfNotDuplicate(_forwardRefs, node.NodeId, entry);
                        var inverse = new ReferenceEntry
                        {
                            SourceNodeId = r.TargetId,
                            ReferenceTypeId = r.ReferenceTypeId,
                            TargetNodeId = node.NodeId,
                            IsForward = false
                        };
                        AddIfNotDuplicate(_inverseRefs, r.TargetId, inverse);
                    }
                    else
                    {
                        AddIfNotDuplicate(_inverseRefs, node.NodeId, entry);
                        var forward = new ReferenceEntry
                        {
                            SourceNodeId = r.TargetId,
                            ReferenceTypeId = r.ReferenceTypeId,
                            TargetNodeId = node.NodeId,
                            IsForward = true
                        };
                        AddIfNotDuplicate(_forwardRefs, r.TargetId, forward);
                    }
                }
            }

            if (node.Children != null)
            {
                AddChildren(node.Children, node.NodeId);
            }
        }

        private void AddChildren(ChildList children, string parentNodeId)
        {
            if (children.Variables != null)
            {
                foreach (var child in children.Variables)
                    AddNodeInternal(child, parentNodeId);
            }
            if (children.Objects != null)
            {
                foreach (var child in children.Objects)
                    AddNodeInternal(child, parentNodeId);
            }
            if (children.Methods != null)
            {
                foreach (var child in children.Methods)
                    AddNodeInternal(child, parentNodeId);
            }
        }

        public void AddReference(string sourceNodeId, string referenceTypeId, string targetNodeId, bool isForward)
        {
            if (!_nodes.TryGetValue(sourceNodeId, out var sourceNode))
                throw new KeyNotFoundException($"Source node '{sourceNodeId}' not found.");

            // Add to the UANode.References list (for serialization)
            sourceNode.References ??= new List<Reference>();
            sourceNode.References.Add(new Reference
            {
                ReferenceTypeId = referenceTypeId,
                TargetId = targetNodeId,
                IsForward = isForward,
            });

            // Index in forward/inverse dictionaries
            var entry = new ReferenceEntry
            {
                SourceNodeId = sourceNodeId,
                ReferenceTypeId = referenceTypeId,
                TargetNodeId = targetNodeId,
                IsForward = isForward,
            };

            if (isForward)
            {
                AddIfNotDuplicate(_forwardRefs, sourceNodeId, entry);
                AddIfNotDuplicate(_inverseRefs, targetNodeId, new ReferenceEntry
                {
                    SourceNodeId = targetNodeId,
                    ReferenceTypeId = referenceTypeId,
                    TargetNodeId = sourceNodeId,
                    IsForward = false,
                });
            }
            else
            {
                AddIfNotDuplicate(_inverseRefs, sourceNodeId, entry);
                AddIfNotDuplicate(_forwardRefs, targetNodeId, new ReferenceEntry
                {
                    SourceNodeId = targetNodeId,
                    ReferenceTypeId = referenceTypeId,
                    TargetNodeId = sourceNodeId,
                    IsForward = true,
                });
            }

            InvalidateSupertypeCache();
        }

        public bool RemoveReference(string sourceNodeId, string referenceTypeId, string targetNodeId, bool isForward)
        {
            if (!_nodes.TryGetValue(sourceNodeId, out var sourceNode))
                return false;

            // Remove from UANode.References
            sourceNode.References?.RemoveAll(r =>
                r.ReferenceTypeId == referenceTypeId &&
                r.TargetId == targetNodeId &&
                (r.IsForward ?? true) == isForward);

            // Remove from index
            if (isForward)
            {
                RemoveFromRefDict(_forwardRefs, sourceNodeId, referenceTypeId, targetNodeId, true);
                RemoveFromRefDict(_inverseRefs, targetNodeId, referenceTypeId, sourceNodeId, false);
            }
            else
            {
                RemoveFromRefDict(_inverseRefs, sourceNodeId, referenceTypeId, targetNodeId, false);
                RemoveFromRefDict(_forwardRefs, targetNodeId, referenceTypeId, sourceNodeId, true);
            }

            InvalidateSupertypeCache();
            return true;
        }

        private static void RemoveFromRefDict(Dictionary<string, List<ReferenceEntry>> dict, string key,
            string referenceTypeId, string targetNodeId, bool isForward)
        {
            if (!dict.TryGetValue(key, out var list)) return;
            list.RemoveAll(e =>
                e.ReferenceTypeId == referenceTypeId &&
                e.TargetNodeId == targetNodeId &&
                e.IsForward == isForward);
            if (list.Count == 0) dict.Remove(key);
        }

        #endregion

        private void RemoveChildren(ChildList children)
        {
            if (children.Objects != null)
            {
                foreach (var child in children.Objects.ToList())
                    if (child.NodeId != null) RemoveNode(child.NodeId);
            }
            if (children.Variables != null)
            {
                foreach (var child in children.Variables.ToList())
                    if (child.NodeId != null) RemoveNode(child.NodeId);
            }
            if (children.Methods != null)
            {
                foreach (var child in children.Methods.ToList())
                    if (child.NodeId != null) RemoveNode(child.NodeId);
            }
        }

        private static void AddIfNotDuplicate(Dictionary<string, List<ReferenceEntry>> dict, string key, ReferenceEntry entry)
        {
            var list = GetOrCreateList(dict, key);
            foreach (var existing in list)
            {
                if (existing.SourceNodeId == entry.SourceNodeId &&
                    existing.ReferenceTypeId == entry.ReferenceTypeId &&
                    existing.TargetNodeId == entry.TargetNodeId &&
                    existing.IsForward == entry.IsForward)
                    return;
            }
            list.Add(entry);
        }

        private static List<T> GetOrCreateList<T>(Dictionary<string, List<T>> dict, string key)
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<T>();
                dict[key] = list;
            }
            return list;
        }
    }
}
