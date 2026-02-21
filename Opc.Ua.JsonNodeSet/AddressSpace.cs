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

        public UANodeSet GetNodeSet(string modelUri)
        {
            var nodeSet = new UANodeSet();

            if (_models.TryGetValue(modelUri, out var model))
            {
                nodeSet.Models = new List<ModelDefinition> { model };
            }
            else
            {
                nodeSet.Models = new List<ModelDefinition>();
            }

            var prefix = $"nsu={modelUri};";
            var filtered = new List<UANode>();
            var filteredIds = new HashSet<string>();

            foreach (var node in _sequence)
            {
                if (node.NodeId != null && node.NodeId.StartsWith(prefix))
                {
                    filtered.Add(node);
                    filteredIds.Add(node.NodeId);
                }
            }

            nodeSet.Nodes = new UANodeSetNodes();

            foreach (var node in filtered)
            {
                if (node.ParentId != null && filteredIds.Contains(node.ParentId))
                    continue;

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
