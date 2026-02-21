using Newtonsoft.Json;
using Opc.Ua.JsonNodeSet.Model;

namespace NodeSetTool
{
    public class ReferenceEntry
    {
        public string SourceNodeId { get; set; }
        public string ReferenceTypeId { get; set; }
        public string TargetNodeId { get; set; }
        public bool IsForward { get; set; }
    }

    public class Subscription : IDisposable
    {
        private Action _removeAction;

        public Subscription(Action removeAction)
        {
            _removeAction = removeAction;
        }

        public void Dispose()
        {
            var action = _removeAction;
            _removeAction = null;
            action?.Invoke();
        }
    }

    public class AddressSpace
    {
        private readonly Dictionary<string, UANode> _nodes = new();
        private readonly Dictionary<string, List<ReferenceEntry>> _forwardRefs = new();
        private readonly Dictionary<string, List<ReferenceEntry>> _inverseRefs = new();
        private readonly Dictionary<string, List<(Guid Id, Action<UANode> Callback)>> _subscriptions = new();
        private readonly List<ModelDefinition> _models = new();
        private readonly List<UANode> _sequence = new();

        public int NodeCount => _nodes.Count;
        public IReadOnlyList<ModelDefinition> Models => _models;
        public IReadOnlyList<UANode> Nodes => _sequence;

        public void AddNode(UANode node, string parentNodeId = null)
        {
            if (node.NodeId == null) return;

            if (parentNodeId != null && node.ParentId == null)
            {
                node.ParentId = parentNodeId;
            }

            _nodes[node.NodeId] = node;
            _sequence.Add(node);

            if (node.References != null)
            {
                foreach (var r in node.References)
                {
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

            // Recurse into children
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
                    AddNode(child, parentNodeId);
            }
            if (children.Objects != null)
            {
                foreach (var child in children.Objects)
                    AddNode(child, parentNodeId);
            }
            if (children.Methods != null)
            {
                foreach (var child in children.Methods)
                    AddNode(child, parentNodeId);
            }
        }

        public List<ReferenceEntry> Browse(string nodeId, string referenceTypeId = null, bool includeForward = true, bool includeInverse = true)
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

        public UANode Read(string nodeId)
        {
            _nodes.TryGetValue(nodeId, out var node);
            return node;
        }

        public bool Write(string nodeId, Variant value)
        {
            if (!_nodes.TryGetValue(nodeId, out var node))
                return false;

            if (node is UAVariable variable)
            {
                variable.Value = value;
                FireSubscriptions(nodeId, node);
                return true;
            }

            return false;
        }

        public IDisposable Subscribe(string nodeId, Action<UANode> callback)
        {
            var id = Guid.NewGuid();
            var list = GetOrCreateList(_subscriptions, nodeId);
            list.Add((id, callback));

            return new Subscription(() =>
            {
                if (_subscriptions.TryGetValue(nodeId, out var subs))
                {
                    subs.RemoveAll(s => s.Id == id);
                }
            });
        }

        private void FireSubscriptions(string nodeId, UANode node)
        {
            if (_subscriptions.TryGetValue(nodeId, out var subs))
            {
                // Iterate over a copy in case a callback modifies the list
                foreach (var (_, callback) in subs.ToList())
                {
                    callback(node);
                }
            }
        }

        public void LoadJson(string filePath)
        {
            using var reader = new JsonTextReader(new StreamReader(filePath));
            LoadJson(reader);
        }

        public void LoadJson(JsonTextReader reader)
        {
            var arrayTypeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
            {
                ["N1ReferenceTypes"] = typeof(UAReferenceType),
                ["N2DataTypes"] = typeof(UADataType),
                ["N3VariableTypes"] = typeof(UAVariableType),
                ["N4ObjectTypes"] = typeof(UAObjectType),
                ["N5Variables"] = typeof(UAVariable),
                ["N6Methods"] = typeof(UAMethod),
                ["N7Objects"] = typeof(UAObject),
                ["N8Views"] = typeof(UAView),
            };

            // Expect the outer object
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.PropertyName)
                {
                    string propName = reader.Value.ToString();

                    if (string.Equals(propName, "Models", StringComparison.OrdinalIgnoreCase))
                    {
                        reader.Read(); // StartArray
                        if (reader.TokenType == JsonToken.StartArray)
                        {
                            while (reader.Read())
                            {
                                if (reader.TokenType == JsonToken.EndArray) break;
                                if (reader.TokenType == JsonToken.StartObject)
                                {
                                    var model = EventDrivenParser.ParseObject<ModelDefinition>(reader);
                                    _models.Add(model);
                                }
                            }
                        }
                    }
                    else if (string.Equals(propName, "Nodes", StringComparison.OrdinalIgnoreCase))
                    {
                        reader.Read(); // StartObject for the Nodes wrapper
                        if (reader.TokenType == JsonToken.StartObject)
                        {
                            ReadNodesObject(reader, arrayTypeMap);
                        }
                    }
                    else
                    {
                        reader.Read();
                        reader.Skip();
                    }
                }
            }
        }

        private void ReadNodesObject(JsonTextReader reader, Dictionary<string, Type> arrayTypeMap)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject) break;

                if (reader.TokenType == JsonToken.PropertyName)
                {
                    string propName = reader.Value.ToString();

                    if (arrayTypeMap.TryGetValue(propName, out var nodeType))
                    {
                        reader.Read(); // StartArray
                        if (reader.TokenType == JsonToken.StartArray)
                        {
                            while (reader.Read())
                            {
                                if (reader.TokenType == JsonToken.EndArray) break;
                                if (reader.TokenType == JsonToken.StartObject)
                                {
                                    var node = (UANode)EventDrivenParser.ParseObject(reader, nodeType);
                                    AddNode(node);
                                }
                            }
                        }
                    }
                    else
                    {
                        reader.Read();
                        reader.Skip();
                    }
                }
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
