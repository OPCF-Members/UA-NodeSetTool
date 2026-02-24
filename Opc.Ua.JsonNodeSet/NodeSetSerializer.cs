using Xml = Opc.Ua.Export;
using Json = Opc.Ua.JsonNodeSet.Model;
using Opc.Ua;
using Opc.Ua.JsonNodeSet;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using SharpCompress.Writers;
using SharpCompress.Common;
using System.Formats.Tar;
using System.Xml.Linq;
using System.Collections.ObjectModel;

namespace NodeSetTool
{
    public class NodeSetSerializer
    {
        private ServiceMessageContext? m_context;
        private Dictionary<string, string>? m_aliases;
        private Dictionary<string, Json.ModelDefinition>? m_models;
        private Dictionary<string, Json.UANode>? m_nodes;
        private List<Json.UANode>? m_sequence;
        private List<CompareError> m_errors = new();
        private List<Json.UANode>? m_stubs;
        private List<string>? m_externalNodeIds;
        private Dictionary<string, string>? m_nsPrefixes; // namespace URI → CURIE prefix

        public IReadOnlyCollection<Json.ModelDefinition> Models => m_models?.Values ?? (IReadOnlyCollection<Json.ModelDefinition>)Array.Empty<Json.ModelDefinition>();

        #region Well-Known Aliases
        private struct AliasToUse
        {
            public AliasToUse(string alias, string nodeId)
            {
                Alias = alias;
                NodeId = nodeId;
            }

            public string Alias;
            public string NodeId;
        }

        private AliasToUse[] s_AliasesToUse = new AliasToUse[]
        {
            new AliasToUse(BrowseNames.Boolean, DataTypeIds.Boolean),
            new AliasToUse(BrowseNames.SByte, DataTypeIds.SByte),
            new AliasToUse(BrowseNames.Byte, DataTypeIds.Byte),
            new AliasToUse(BrowseNames.Int16, DataTypeIds.Int16),
            new AliasToUse(BrowseNames.UInt16, DataTypeIds.UInt16),
            new AliasToUse(BrowseNames.Int32, DataTypeIds.Int32),
            new AliasToUse(BrowseNames.UInt32, DataTypeIds.UInt32),
            new AliasToUse(BrowseNames.Int64, DataTypeIds.Int64),
            new AliasToUse(BrowseNames.UInt64, DataTypeIds.UInt64),
            new AliasToUse(BrowseNames.Float, DataTypeIds.Float),
            new AliasToUse(BrowseNames.Double, DataTypeIds.Double),
            new AliasToUse(BrowseNames.DateTime, DataTypeIds.DateTime),
            new AliasToUse(BrowseNames.String, DataTypeIds.String),
            new AliasToUse(BrowseNames.ByteString, DataTypeIds.ByteString),
            new AliasToUse(BrowseNames.Guid, DataTypeIds.Guid),
            new AliasToUse(BrowseNames.XmlElement, DataTypeIds.XmlElement),
            new AliasToUse(BrowseNames.NodeId, DataTypeIds.NodeId),
            new AliasToUse(BrowseNames.ExpandedNodeId, DataTypeIds.ExpandedNodeId),
            new AliasToUse(BrowseNames.QualifiedName, DataTypeIds.QualifiedName),
            new AliasToUse(BrowseNames.LocalizedText, DataTypeIds.LocalizedText),
            new AliasToUse(BrowseNames.StatusCode, DataTypeIds.StatusCode),
            new AliasToUse(BrowseNames.Structure, DataTypeIds.Structure),
            new AliasToUse(BrowseNames.Number, DataTypeIds.Number),
            new AliasToUse(BrowseNames.Integer, DataTypeIds.Integer),
            new AliasToUse(BrowseNames.UInteger, DataTypeIds.UInteger),
            new AliasToUse(BrowseNames.HasComponent, ReferenceTypeIds.HasComponent),
            new AliasToUse(BrowseNames.HasProperty, ReferenceTypeIds.HasProperty),
            new AliasToUse(BrowseNames.Organizes, ReferenceTypeIds.Organizes),
            new AliasToUse(BrowseNames.HasEventSource, ReferenceTypeIds.HasEventSource),
            new AliasToUse(BrowseNames.HasNotifier, ReferenceTypeIds.HasNotifier),
            new AliasToUse(BrowseNames.HasSubtype, ReferenceTypeIds.HasSubtype),
            new AliasToUse(BrowseNames.HasTypeDefinition, ReferenceTypeIds.HasTypeDefinition),
            new AliasToUse(BrowseNames.HasModellingRule, ReferenceTypeIds.HasModellingRule),
            new AliasToUse(BrowseNames.HasEncoding, ReferenceTypeIds.HasEncoding),
            new AliasToUse(BrowseNames.HasDescription, ReferenceTypeIds.HasDescription),
            new AliasToUse(BrowseNames.HasCause, ReferenceTypeIds.HasCause),
            new AliasToUse(BrowseNames.ToState, ReferenceTypeIds.ToState),
            new AliasToUse(BrowseNames.FromState, ReferenceTypeIds.FromState),
            new AliasToUse(BrowseNames.HasEffect, ReferenceTypeIds.HasEffect),
            new AliasToUse(BrowseNames.HasTrueSubState, ReferenceTypeIds.HasTrueSubState),
            new AliasToUse(BrowseNames.HasFalseSubState, ReferenceTypeIds.HasFalseSubState),
            new AliasToUse(BrowseNames.HasDictionaryEntry, ReferenceTypeIds.HasDictionaryEntry),
            new AliasToUse(BrowseNames.HasCondition, ReferenceTypeIds.HasCondition),
            new AliasToUse(BrowseNames.HasGuard, ReferenceTypeIds.HasGuard),
            new AliasToUse(BrowseNames.HasAddIn, ReferenceTypeIds.HasAddIn),
            new AliasToUse(BrowseNames.HasInterface, ReferenceTypeIds.HasInterface),
            new AliasToUse(BrowseNames.GeneratesEvent, ReferenceTypeIds.GeneratesEvent),
            new AliasToUse(BrowseNames.AlwaysGeneratesEvent, ReferenceTypeIds.AlwaysGeneratesEvent),
            new AliasToUse(BrowseNames.HasOrderedComponent, ReferenceTypeIds.HasOrderedComponent),
            new AliasToUse(BrowseNames.HasAlarmSuppressionGroup, ReferenceTypeIds.HasAlarmSuppressionGroup),
            new AliasToUse(BrowseNames.AlarmGroupMember, ReferenceTypeIds.AlarmGroupMember),
            new AliasToUse(BrowseNames.AlarmSuppressionGroupMember, ReferenceTypeIds.AlarmSuppressionGroupMember)
        };
        #endregion

        #region NodeSet Comparisons
        public ReadOnlyCollection<CompareError> CompareErrors => new(m_errors);

        public bool Compare(NodeSetSerializer target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            m_errors = new();

            if (!CompareModels(target))
            {
                return false;
            }

            foreach (var node in m_sequence!)
            {
                if (!target.m_nodes!.TryGetValue(node.NodeId!, out var match))
                {
                    m_errors.Add(new CompareError(node, "Node Not Found", node.NodeId, null));
                    return false;
                }

                if (!Compare(node, match))
                {
                    return false;
                }
            }

            foreach (var node in target.m_sequence!)
            {
                if (!m_nodes!.TryGetValue(node.NodeId!, out var match))
                {
                    m_errors.Add(new CompareError(node, "Extra Node Found", node.NodeId, null));
                    return false;
                }

                if (!Compare(node, match))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CompareModels(NodeSetSerializer target)
        {
            if (m_models!.Count != target.m_models!.Count)
            {
                m_errors.Add(new CompareError(null, "Model Count", m_models.Count, target.m_models.Count));
                return false;
            }

            foreach (var kvp in m_models)
            {
                if (!target.m_models.TryGetValue(kvp.Key, out var targetModel))
                {
                    m_errors.Add(new CompareError(null, "Model Not Found", kvp.Key, null));
                    return false;
                }

                var srcModel = kvp.Value;

                if (!CompareModelRef(srcModel, targetModel, srcModel.ModelUri!))
                {
                    return false;
                }

                if (!CompareRequiredModels(srcModel.RequiredModels, targetModel.RequiredModels, srcModel.ModelUri!))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CompareModelRef(Json.ModelReference original, Json.ModelReference target, string context)
        {
            if (original.ModelUri != target.ModelUri)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' ModelUri", original.ModelUri, target.ModelUri));
                return false;
            }

            if (original.XmlSchemaUri != target.XmlSchemaUri)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' XmlSchemaUri", original.XmlSchemaUri, target.XmlSchemaUri));
                return false;
            }

            if (original.VarVersion != target.VarVersion)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' Version", original.VarVersion, target.VarVersion));
                return false;
            }

            if (original.ModelVersion != target.ModelVersion)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' ModelVersion", original.ModelVersion, target.ModelVersion));
                return false;
            }

            if (original.PublicationDate != target.PublicationDate)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' PublicationDate", original.PublicationDate, target.PublicationDate));
                return false;
            }

            return true;
        }

        private bool CompareRequiredModels(List<Json.ModelReference>? original, List<Json.ModelReference>? target, string context)
        {
            if (original == null && target == null) return true;

            if (original == null || target == null)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' RequiredModels", original?.Count, target?.Count));
                return false;
            }

            if (original.Count != target.Count)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' RequiredModels Count", original.Count, target.Count));
                return false;
            }

            // Order-independent comparison: match by ModelUri
            var unmatched = new List<Json.ModelReference>(target);

            foreach (var src in original)
            {
                var match = unmatched.FirstOrDefault(t => t.ModelUri == src.ModelUri);

                if (match == null)
                {
                    m_errors.Add(new CompareError(null, $"Model '{context}' RequiredModel Not Found", src.ModelUri, null));
                    return false;
                }

                if (!CompareModelRef(src, match, $"{context} -> {src.ModelUri}"))
                {
                    return false;
                }

                unmatched.Remove(match);
            }

            return true;
        }

        public class CompareError
        {
            public CompareError(Json.UANode? node, string fieldName, object? original, object? target)
            {
                Node = node;
                Message = fieldName;
                Original = original;
                Target = target;
            }

            public Json.UANode? Node { get; }

            public string Message { get; }

            public object? Original { get; }

            public object? Target { get; }

            public override string ToString()
            {
                return $"{Node?.BrowseName} [{Node?.NodeId}] {Message}: {Original} != {Target}";
            }
        }

        private bool Compare(Json.UANode? original, Json.UANode? target)
        {
            if (original == null || target == null) return false;

            if (original.NodeId != target.NodeId) { m_errors.Add(new CompareError(original, nameof(Json.UANode.NodeId), original.NodeId, target.NodeId)); return false; }
            if (original.NodeClass != target.NodeClass) { m_errors.Add(new CompareError(original, nameof(Json.UANode.NodeClass), original.NodeClass, target.NodeClass)); return false; }
            if (original.SymbolicName != target.SymbolicName) { m_errors.Add(new CompareError(original, nameof(Json.UANode.SymbolicName), original.SymbolicName, target.SymbolicName)); return false; }
            if (original.BrowseName != target.BrowseName) { m_errors.Add(new CompareError(original, nameof(Json.UANode.BrowseName), original.BrowseName, target.BrowseName)); return false; }
            if (!Compare(original.DisplayName, target.DisplayName)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.DisplayName), original.DisplayName, target.DisplayName)); return false; }
            if (!Compare(original.Description, target.Description)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.Description), original.Description, target.Description)); return false; }
            if (original.WriteMask != target.WriteMask) { m_errors.Add(new CompareError(original, nameof(Json.UANode.WriteMask), original.WriteMask, target.WriteMask)); return false; }
            if (original.Documentation != target.Documentation) { m_errors.Add(new CompareError(original, nameof(Json.UANode.Documentation), original.Documentation, target.Documentation)); return false; }
            if (original.AccessRestrictions != target.AccessRestrictions) { m_errors.Add(new CompareError(original, nameof(Json.UANode.AccessRestrictions), original.AccessRestrictions, target.AccessRestrictions)); return false; }
            if (original.HasNoPermissions != target.HasNoPermissions) { m_errors.Add(new CompareError(original, nameof(Json.UANode.HasNoPermissions), original.HasNoPermissions, target.HasNoPermissions)); return false; }
            if (original.ParentId != target.ParentId) { m_errors.Add(new CompareError(original, nameof(Json.UANode.ParentId), original.ParentId, target.ParentId)); return false; }
            if (original.IsAbstract != target.IsAbstract) { m_errors.Add(new CompareError(original, nameof(Json.UANode.IsAbstract), original.IsAbstract, target.IsAbstract)); return false; }
            if (original.DesignToolOnly != target.DesignToolOnly) { m_errors.Add(new CompareError(original, nameof(Json.UANode.DesignToolOnly), original.DesignToolOnly, target.DesignToolOnly)); return false; }
            if (original.ModellingRuleId != target.ModellingRuleId) { m_errors.Add(new CompareError(original, nameof(Json.UANode.ModellingRuleId), original.ModellingRuleId, target.ModellingRuleId)); return false; }
            if (original.ReleaseStatus != target.ReleaseStatus) { m_errors.Add(new CompareError(original, nameof(Json.UANode.ReleaseStatus), original.ReleaseStatus, target.ReleaseStatus)); return false; }
            if (!Compare(original.ConformationUnits, target.ConformationUnits)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.ConformationUnits), original.ConformationUnits, target.ConformationUnits)); return false; }
            if (!Compare(original, original.References, target.References)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.References), original.References, target.References)); return false; }
            if (!Compare(original, original.Children, target.Children)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.Children), original.Children, target.Children)); return false; }
            if (!Compare(original.RolePermissions, target.RolePermissions)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.RolePermissions), original.RolePermissions, target.RolePermissions)); return false; }

            return true;
        }

        private bool Compare(IList<Json.RolePermission>? original, IList<Json.RolePermission>? target)
        {
            if (original == null || target == null) return Object.ReferenceEquals(original, target);

            if (original.Count != target.Count)
            {
                return false;
            }

            for (int ii = 0; ii < original.Count; ii++)
            {
                if (original[ii].Permissions != target[ii].Permissions) return false;
                if (original[ii].RoleId != target[ii].RoleId) return false;
            }

            return true;
        }

        private bool Compare(Json.UANode? context, Json.ChildList? original, Json.ChildList? target)
        {
            if (original == null || target == null) return Object.ReferenceEquals(original, target);

            if (original.Objects != null && target.Objects != null)
            {
                if (original.Objects.Count != target.Objects.Count)
                {
                    m_errors.Add(new CompareError(context, "ChildList.Objects.Count", original.Objects.Count, target.Objects.Count));
                    return false;
                }

                for (int ii = 0; ii < original.Objects.Count; ii++)
                {
                    if (original.Objects[ii] == null || target.Objects[ii] == null || !Compare(original.Objects[ii], target.Objects[ii]))
                    {
                        return false;
                    }
                }
            }
            else if (original.Objects != null || target.Objects != null)
            {
                return false;
            }

            if (original.Variables != null && target.Variables != null)
            {
                if (original.Variables.Count != target.Variables.Count)
                {
                    m_errors.Add(new CompareError(context, "ChildList.Variables.Count", original.Variables.Count, target.Variables.Count));
                    return false;
                }

                for (int ii = 0; ii < original.Variables.Count; ii++)
                {
                    if (original.Variables[ii] == null || target.Variables[ii] == null || !Compare(original.Variables[ii], target.Variables[ii]))
                    {
                        return false;
                    }
                }
            }
            else if (original.Variables != null || target.Variables != null)
            {
                return false;
            }

            if (original.Methods != null && target.Methods != null)
            {
                if (original.Methods.Count != target.Methods.Count)
                {
                    m_errors.Add(new CompareError(context, "ChildList.Methods.Count", original.Methods.Count, target.Methods.Count));
                    return false;
                }

                for (int ii = 0; ii < original.Methods.Count; ii++)
                {
                    if (original.Methods[ii] == null || target.Methods[ii] == null || !Compare(original.Methods[ii], target.Methods[ii]))
                    {
                        return false;
                    }
                }
            }
            else if (original.Methods != null || target.Methods != null)
            {
                return false;
            }

            return true;
        }

        private bool Compare(Json.UANode? context, IList<Json.Reference>? original, IList<Json.Reference>? target)
        {
            if (original == null || target == null)
            {
                if (original != null && original.Count == 0)
                {
                    return true;
                }

                return (target != null && target.Count == 0);
            }

            if (original.Count != target.Count)
            {
                m_errors.Add(new CompareError(context, "ReferenceList", original.Count, target.Count));
                return false;
            }

            for (int ii = 0; ii < original.Count; ii++)
            {
                if (original[ii].ReferenceTypeId != target[ii].ReferenceTypeId)
                {
                    m_errors.Add(new CompareError(context, "Reference.ReferenceTypeId", original[ii].ReferenceTypeId, target[ii].ReferenceTypeId));
                    return false;
                }

                if (original[ii].IsForward != target[ii].IsForward)
                {
                    m_errors.Add(new CompareError(context, "Reference.IsForward", original[ii].IsForward, target[ii].IsForward));
                    return false;
                }

                if (original[ii].TargetId != target[ii].TargetId)
                {
                    m_errors.Add(new CompareError(context, "Reference.TargetId", original[ii].TargetId, target[ii].TargetId));
                    return false;
                }
            }

            return true;
        }

        private bool Compare(IList<string>? original, IList<string>? target)
        {
            if (original == null || target == null) return Object.ReferenceEquals(original, target);
            return original.SequenceEqual(target);
        }

        private bool Compare(Json.LocalizedText? original, Json.LocalizedText? target)
        {
            if (original == null || target == null) return Object.ReferenceEquals(original, target);

            if (original.T == null || target.T == null)
            {
                return Object.ReferenceEquals(original.T, target.T);
            }

            if (original.T.Count != target.T.Count)
            {
                return false;
            }

            for (int ii = 0; ii < original.T.Count; ii++)
            {
                if (original.T[ii] == null || target.T[ii] == null || !original.T[ii].SequenceEqual(target.T[ii]))
                {
                    return false;
                }
            }

            if (original.R == null || target.R == null)
            {
                return Object.ReferenceEquals(original.R, target.R);
            }

            if (original.R.Count != target.R.Count)
            {
                return false;
            }

            for (int ii = 0; ii < original.R.Count; ii++)
            {
                if (original.R[ii] == null || target.R[ii] == null || !original.R[ii].SequenceEqual(target.R[ii]))
                {
                    return false;
                }
            }

            return true;
        }
        #endregion

        public void Load(string filePath)
        {
            if (filePath.EndsWith(".xml"))
            {
                LoadXml(filePath);
                return;
            }
            else if (filePath.EndsWith(".jsonld"))
            {
                LoadJsonLd(filePath);
                return;
            }
            else if (filePath.EndsWith(".json"))
            {
                LoadJson(filePath);
                return;
            }
            else if (filePath.EndsWith(".tar.gz"))
            {
                LoadArchive(filePath);
                return;
            }

            if (IsValidXml(filePath))
            {
                LoadXml(filePath);
                return;
            }

            if (IsValidJson(filePath))
            {
                LoadJson(filePath);
                return;
            }

            LoadArchive(filePath);
        }

        private static bool IsValidXml(string filePath)
        {
            try
            {
                // Load the file as an XDocument. This will throw an exception if the file is not valid XML.
                XDocument.Load(filePath);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsValidJson(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);

                // Early exit if content is null or whitespace.
                if (String.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                // Attempt to deserialize into a dynamic object.
                JsonConvert.DeserializeObject(json);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void LoadXml(string filePath)
        {
            using var istrm = File.OpenRead(filePath);
            var input = Xml.UANodeSet.Read(istrm)!;
            Initialize(input);
        }

        public void LoadXml(Stream stream)
        {
            var input = Xml.UANodeSet.Read(stream)!;
            Initialize(input);
        }

        public void SaveXml(string filePath)
        {
            var xml = BuildXml();
            using var ostrm = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite);
            xml.Write(ostrm);
        }

        public void SaveXml(Stream stream)
        {
            var xml = BuildXml();
            xml.Write(stream);
        }

        public void SaveJson(string filePath)
        {
            JsonSerializer serializer = new JsonSerializer
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.Indented,
                DefaultValueHandling = DefaultValueHandling.Ignore,
            };

            var nodeset = BuildJson();

            using (StreamWriter file = File.CreateText(filePath))
            using (JsonTextWriter writer = new JsonTextWriter(file))
            {
                serializer.Serialize(writer, nodeset);
            }
        }

        public void SaveJson(Stream stream)
        {
            var serializer = new JsonSerializer
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.Indented,
                DefaultValueHandling = DefaultValueHandling.Ignore,
            };

            var nodeset = BuildJson();

            using var writer = new StreamWriter(stream, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(writer);
            serializer.Serialize(jsonWriter, nodeset);
        }

        public void LoadJson(string filePath)
        {
            LoadWellKnownAliases();

            using (FileStream fs = File.OpenRead(filePath))
            using (StreamReader js = new StreamReader(fs))
            using (JsonTextReader reader = new JsonTextReader(js))
            {
                var serializer = new JsonSerializer();
                var nodeset = serializer.Deserialize<Json.UANodeSet>(reader)!;
                IndexFile(nodeset);
            }
        }

        private void LoadWellKnownAliases()
        {
            m_aliases = new();

            foreach (var alias in s_AliasesToUse)
            {
                m_aliases[alias.Alias] = alias.NodeId;
            }
        }

        public void LoadArchive(string filePath)
        {
            LoadWellKnownAliases();

            List<Json.UANodeSet> files = new();

            using (FileStream fs = File.OpenRead(filePath))
            using (GZipStream gzipStream = new GZipStream(fs, CompressionMode.Decompress))
            using (TarReader tarReader = new TarReader(gzipStream))
            {
                TarEntry? entry;

                while ((entry = tarReader.GetNextEntry()) != null)
                {
                    if (entry.EntryType == TarEntryType.V7RegularFile || entry.EntryType == TarEntryType.RegularFile)
                    {
                        using (var ms = new MemoryStream())
                        {
                            entry.DataStream!.CopyTo(ms);
                            ms.Position = 0;

                            using (StreamReader js = new StreamReader(ms))
                            using (JsonTextReader reader = new JsonTextReader(js))
                            {
                                var serializer = new JsonSerializer();
                                var file = serializer.Deserialize<Json.UANodeSet>(reader)!;
                                files.Add(file);
                            }
                        }
                    }
                }
            }

            Json.UANodeSet nodeset = new Json.UANodeSet();

            foreach (var file in files.OrderBy(x => x.FileSet?.Current))
            {
                if (nodeset.Models == null) nodeset.Models = file.Models;
                if (nodeset.Nodes == null) nodeset.Nodes = new();

                if (file.Nodes!.N1ReferenceTypes != null)
                {
                    if (nodeset.Nodes!.N1ReferenceTypes == null) nodeset.Nodes.N1ReferenceTypes = new();
                    nodeset.Nodes.N1ReferenceTypes.AddRange(file.Nodes.N1ReferenceTypes);
                }

                if (file.Nodes.N2DataTypes != null)
                {
                    if (nodeset.Nodes!.N2DataTypes == null) nodeset.Nodes.N2DataTypes = new();
                    nodeset.Nodes.N2DataTypes.AddRange(file.Nodes.N2DataTypes);
                }

                if (file.Nodes.N3VariableTypes != null)
                {
                    if (nodeset.Nodes!.N3VariableTypes == null) nodeset.Nodes.N3VariableTypes = new();
                    nodeset.Nodes.N3VariableTypes.AddRange(file.Nodes.N3VariableTypes);
                }

                if (file.Nodes.N4ObjectTypes != null)
                {
                    if (nodeset.Nodes!.N4ObjectTypes == null) nodeset.Nodes.N4ObjectTypes = new();
                    nodeset.Nodes.N4ObjectTypes.AddRange(file.Nodes.N4ObjectTypes);
                }

                if (file.Nodes.N5Variables != null)
                {
                    if (nodeset.Nodes!.N5Variables == null) nodeset.Nodes.N5Variables = new();
                    nodeset.Nodes.N5Variables.AddRange(file.Nodes.N5Variables);
                }

                if (file.Nodes.N6Methods != null)
                {
                    if (nodeset.Nodes!.N6Methods == null) nodeset.Nodes.N6Methods = new();
                    nodeset.Nodes.N6Methods.AddRange(file.Nodes.N6Methods);
                }

                if (file.Nodes.N7Objects != null)
                {
                    if (nodeset.Nodes!.N7Objects == null) nodeset.Nodes.N7Objects = new();
                    nodeset.Nodes.N7Objects.AddRange(file.Nodes.N7Objects);
                }

                if (file.Nodes.N8Views != null)
                {
                    if (nodeset.Nodes!.N8Views == null) nodeset.Nodes.N8Views = new();
                    nodeset.Nodes.N8Views.AddRange(file.Nodes.N8Views);
                }
            }

            IndexFile(nodeset);
        }

        public void SaveArchive(string filePath, int maxNodesPerFile)
        {
            if (File.Exists(filePath)) File.Delete(filePath);

            var nodeset = BuildJson();
            var files = Package(nodeset, maxNodesPerFile);

            using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (GZipStream gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
            using (var tarWriter = WriterFactory.Open(gzipStream, ArchiveType.Tar, CompressionType.None))
            {
                foreach (var file in files)
                {
                    JsonSerializer serializer = new JsonSerializer
                    {
                        Formatting = Newtonsoft.Json.Formatting.None,
                        NullValueHandling = NullValueHandling.Ignore,
                        DefaultValueHandling = DefaultValueHandling.Ignore,
                    };

                    using (var memoryStream = new MemoryStream())
                    using (StreamWriter jsonStream = new StreamWriter(memoryStream))
                    using (JsonTextWriter writer = new JsonTextWriter(jsonStream))
                    {
                        serializer.Serialize(writer, file);
                        writer.Flush();
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        tarWriter.Write($"UANodeSet_{file.FileSet!.Current:D3}_Of_{file.FileSet.Last:D3}.json", memoryStream, null); // null = use defaults for entry metadata
                    }
                }
            }
        }

        public void SaveArchive(Stream stream, int maxNodesPerFile)
        {
            var nodeset = BuildJson();
            var files = Package(nodeset, maxNodesPerFile);

            using var gzipStream = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
            using var tarWriter = WriterFactory.Open(gzipStream, ArchiveType.Tar, CompressionType.None);

            foreach (var file in files)
            {
                var serializer = new JsonSerializer
                {
                    Formatting = Newtonsoft.Json.Formatting.None,
                    NullValueHandling = NullValueHandling.Ignore,
                    DefaultValueHandling = DefaultValueHandling.Ignore,
                };

                using var ms = new MemoryStream();
                using var sw = new StreamWriter(ms);
                using var jw = new JsonTextWriter(sw);
                serializer.Serialize(jw, file);
                jw.Flush();
                ms.Seek(0, SeekOrigin.Begin);
                tarWriter.Write($"UANodeSet_{file.FileSet!.Current:D3}_Of_{file.FileSet.Last:D3}.json", ms, null);
            }
        }

        private void IndexChildren(Json.UANode parent)
        {
            if (parent == null || parent.Children == null)
            {
                return;
            }

            if (parent.Children.Objects != null)
            {
                foreach (var child in parent.Children.Objects)
                {
                    child.ParentId = parent.NodeId;
                    if (m_nodes!.ContainsKey(child.NodeId!)) continue; // already indexed via flat list
                    m_nodes[child.NodeId!] = child;
                    m_sequence!.Add(child);
                    IndexChildren(child);
                }
            }

            if (parent.Children.Variables != null)
            {
                foreach (var child in parent.Children.Variables)
                {
                    child.ParentId = parent.NodeId;
                    if (m_nodes!.ContainsKey(child.NodeId!)) continue;
                    m_nodes[child.NodeId!] = child;
                    m_sequence!.Add(child);
                    IndexChildren(child);
                }
            }

            if (parent.Children.Methods != null)
            {
                foreach (var child in parent.Children.Methods)
                {
                    child.ParentId = parent.NodeId;
                    if (m_nodes!.ContainsKey(child.NodeId!)) continue;
                    m_nodes[child.NodeId!] = child;
                    m_sequence!.Add(child);
                    IndexChildren(child);
                }
            }
        }

        private void IndexFile(Json.UANodeSet nodeset)
        {
            m_context = new();
            m_models = new();

            if (nodeset.Models != null)
            {
                foreach (var model in nodeset.Models)
                {
                    m_context.NamespaceUris.GetIndexOrAppend(model.ModelUri!);

                    if (model.RequiredModels != null)
                    {
                        foreach (var dependency in model.RequiredModels)
                        {
                            m_context.NamespaceUris.GetIndexOrAppend(dependency.ModelUri!);
                        }
                    }

                    m_models[model.ModelUri!] = model;
                }
            }

            m_nodes = new();
            m_sequence = new();

            if (nodeset.Nodes != null)
            {
                if (nodeset.Nodes!.N1ReferenceTypes != null)
                {
                    foreach (var node in nodeset.Nodes.N1ReferenceTypes)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.N2DataTypes != null)
                {
                    foreach (var node in nodeset.Nodes.N2DataTypes)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.N3VariableTypes != null)
                {
                    foreach (var node in nodeset.Nodes.N3VariableTypes)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.N4ObjectTypes != null)
                {
                    foreach (var node in nodeset.Nodes.N4ObjectTypes)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.N5Variables != null)
                {
                    foreach (var node in nodeset.Nodes.N5Variables)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.N6Methods != null)
                {
                    foreach (var node in nodeset.Nodes.N6Methods)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.N7Objects != null)
                {
                    foreach (var node in nodeset.Nodes.N7Objects)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.N8Views != null)
                {
                    foreach (var node in nodeset.Nodes.N8Views)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }
            }
        }

        private Xml.UANodeSet BuildXml()
        {
            Xml.UANodeSet nodeset = new Xml.UANodeSet();

            List<Xml.ModelTableEntry> models = new();

            foreach (var model in m_models!.Values)
            {
                models.Add(ToXml(model));
            }

            nodeset.Models = models.ToArray();

            List<Xml.UANode> nodes = new();

            foreach (var item in m_sequence!)
            {
                switch (item)
                {
                    case Json.UAReferenceType rt: { nodes.Add(ToXmlNode(rt)); break; }
                    case Json.UADataType dt: { nodes.Add(ToXmlNode(dt)); break; }
                    case Json.UAVariableType vt: { nodes.Add(ToXmlNode(vt)); break; }
                    case Json.UAObjectType ot: { nodes.Add(ToXmlNode(ot)); break; }
                    case Json.UAObject on: { nodes.Add(ToXmlNode(on)); break; }
                    case Json.UAVariable vn: { nodes.Add(ToXmlNode(vn)); break; }
                    case Json.UAMethod mn: { nodes.Add(ToXmlNode(mn)); break; }
                    case Json.UAView wn: { nodes.Add(ToXmlNode(wn)); break; }
                }
            }

            nodeset.Items = nodes.ToArray();
            nodeset.NamespaceUris = m_context!.NamespaceUris.ToArray();
            nodeset.ServerUris = m_context.ServerUris.ToArray();

            List<Xml.NodeIdAlias> aliases = new();

            foreach (var alias in m_aliases!)
            {
                aliases.Add(new Xml.NodeIdAlias() { Alias = alias.Key, Value = alias.Value });
            }

            nodeset.Aliases = aliases.ToArray();

            return nodeset;
        }

        private Json.UANodeSet BuildJson()
        {
            Json.UANodeSet nodeset = new Json.UANodeSet();
            nodeset.Models = new List<Json.ModelDefinition>(m_models!.Values);
            nodeset.Nodes = new();

            foreach (var item in m_sequence!)
            {
                if (item.ParentId != null && m_nodes!.ContainsKey(item.ParentId))
                {
                    continue;
                }

                switch (item)
                {
                    case Json.UAReferenceType rt: { if (nodeset.Nodes.N1ReferenceTypes == null) nodeset.Nodes.N1ReferenceTypes = new(); nodeset.Nodes.N1ReferenceTypes.Add(rt); break; }
                    case Json.UADataType dt: { if (nodeset.Nodes.N2DataTypes == null) nodeset.Nodes.N2DataTypes = new(); nodeset.Nodes.N2DataTypes.Add(dt); break; }
                    case Json.UAVariableType vt: { if (nodeset.Nodes.N3VariableTypes == null) nodeset.Nodes.N3VariableTypes = new(); nodeset.Nodes.N3VariableTypes.Add(vt); break; }
                    case Json.UAObjectType ot: { if (nodeset.Nodes.N4ObjectTypes == null) nodeset.Nodes.N4ObjectTypes = new(); nodeset.Nodes.N4ObjectTypes.Add(ot); break; }
                    case Json.UAVariable vn: { if (nodeset.Nodes.N5Variables == null) nodeset.Nodes.N5Variables = new(); nodeset.Nodes.N5Variables.Add(vn); break; }
                    case Json.UAMethod mn: { if (nodeset.Nodes.N6Methods == null) nodeset.Nodes.N6Methods = new(); nodeset.Nodes.N6Methods.Add(mn); break; }
                    case Json.UAObject on: { if (nodeset.Nodes.N7Objects == null) nodeset.Nodes.N7Objects = new(); nodeset.Nodes.N7Objects.Add(on); break; }
                    case Json.UAView wn: { if (nodeset.Nodes.N8Views == null) nodeset.Nodes.N8Views = new(); nodeset.Nodes.N8Views.Add(wn); break; }
                }
            }

            return nodeset;
        }

        private Json.UANodeSet NewFile(Json.UANodeSet nodeset, int currentFileCount)
        {
            Json.UANodeSet current = new();
            current.Nodes = new();

            current.FileSet = new Json.FileSetInfo()
            {
                Current = currentFileCount + 1,
                Last = 0
            };

            return current;
        }

        private int CountNodes(Json.UANode node)
        {
            if (node?.Children == null)
            {
                return 1;
            }

            int count = 1;

            if (node.Children.Objects != null)
            {
                foreach (var child in node.Children.Objects)
                {
                    count += CountNodes(child);
                }
            }

            if (node.Children.Variables != null)
            {
                foreach (var child in node.Children.Variables)
                {
                    count += CountNodes(child);
                }
            }

            if (node.Children.Methods != null)
            {
                foreach (var child in node.Children.Methods)
                {
                    count += CountNodes(child);
                }
            }

            return count;
        }


        private List<Json.UANodeSet> Package(Json.UANodeSet nodeset, int maxNodesPerFile)
        {
            List<Json.UANodeSet> files = new List<Json.UANodeSet>();

            int count = 0;

            Json.UANodeSet current = NewFile(nodeset, files.Count);
            current.Models = nodeset.Models;

            if (nodeset.Nodes!.N1ReferenceTypes != null)
            {
                current.Nodes!.N1ReferenceTypes = new();

                foreach (var node in nodeset.Nodes.N1ReferenceTypes)
                {
                    current.Nodes!.N1ReferenceTypes.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.N1ReferenceTypes = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.N2DataTypes != null)
            {
                current.Nodes!.N2DataTypes = new();

                foreach (var node in nodeset.Nodes.N2DataTypes)
                {
                    current.Nodes!.N2DataTypes.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.N2DataTypes = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.N3VariableTypes != null)
            {
                current.Nodes!.N3VariableTypes = new();

                foreach (var node in nodeset.Nodes.N3VariableTypes)
                {
                    current.Nodes!.N3VariableTypes.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.N3VariableTypes = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.N4ObjectTypes != null)
            {
                current.Nodes!.N4ObjectTypes = new();

                foreach (var node in nodeset.Nodes.N4ObjectTypes)
                {
                    current.Nodes!.N4ObjectTypes.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.N4ObjectTypes = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.N5Variables != null)
            {
                current.Nodes!.N5Variables = new();

                foreach (var node in nodeset.Nodes.N5Variables)
                {
                    current.Nodes!.N5Variables.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.N5Variables = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.N6Methods != null)
            {
                current.Nodes!.N6Methods = new();

                foreach (var node in nodeset.Nodes.N6Methods)
                {
                    current.Nodes!.N6Methods.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.N6Methods = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.N7Objects != null)
            {
                current.Nodes!.N7Objects = new();

                foreach (var node in nodeset.Nodes.N7Objects)
                {
                    current.Nodes!.N7Objects.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.N7Objects = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.N8Views != null)
            {
                current.Nodes!.N8Views = new();

                foreach (var node in nodeset.Nodes.N8Views)
                {
                    current.Nodes!.N8Views.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.N8Views = new();
                        count = 0;
                    }
                }
            }

            files.Add(current);
            count = 0;

            foreach (var file in files)
            {
                file.FileSet!.Last = files.Count;
            }

            return files;
        }

        private void Initialize(Opc.Ua.Export.UANodeSet input)
        {
            m_context = new ServiceMessageContext();

            if (input.NamespaceUris != null)
            {
                foreach (var uri in input.NamespaceUris)
                {
                    m_context.NamespaceUris.GetIndexOrAppend(uri);
                }
            }

            if (input.ServerUris != null)
            {
                foreach (var uri in input.ServerUris)
                {
                    m_context.ServerUris.GetIndexOrAppend(uri);
                }
            }

            m_aliases = new();

            if (input.Aliases != null)
            {
                foreach (var alias in input.Aliases)
                {
                    m_aliases[alias.Alias!] = alias.Value!;
                }
            }

            m_models = new();

            if (input.Models != null)
            {
                foreach (var item in input.Models)
                {
                    var model = ToJson(item);
                    m_models[model.ModelUri!] = model;
                }
            }

            m_nodes = new();
            m_sequence = new();

            if (input.Items != null)
            {
                foreach (var item in input.Items)
                {
                    var node = ToJson(item)!;
                    m_nodes[node.NodeId!] = node;
                    m_sequence.Add(node);
                }
            }

            foreach (var item in m_sequence)
            {
                if (item.ParentId != null)
                {
                    if (m_nodes.TryGetValue(item.ParentId, out var parent))
                    {
                        if (parent.Children == null)
                        {
                            parent.Children = new Json.ChildList();
                        }

                        switch (item)
                        {
                            case Json.UAObject od: { if (parent.Children.Objects == null) parent.Children.Objects = new(); parent.Children.Objects.Add(od); break; }
                            case Json.UAVariable vn: { if (parent.Children.Variables == null) parent.Children.Variables = new(); parent.Children.Variables.Add(vn); break; }
                            case Json.UAMethod mn: { if (parent.Children.Methods == null) parent.Children.Methods = new(); parent.Children.Methods.Add(mn); break; }
                        }
                    }
                }
            }
        }

        private Json.UANode? ToJson(Xml.UANode input)
        {
            switch (input)
            {
                case Xml.UAObject od: return ToJsonNode(od);
                case Xml.UAVariable vn: return ToJsonNode(vn);
                case Xml.UAMethod mn: return ToJsonNode(mn);
                case Xml.UAObjectType ot: return ToJsonNode(ot);
                case Xml.UAVariableType vt: return ToJsonNode(vt);
                case Xml.UADataType dt: return ToJsonNode(dt);
                case Xml.UAReferenceType rt: return ToJsonNode(rt);
                case Xml.UAView wn: return ToJsonNode(wn);
            }

            return null;
        }

        private Json.ModelDefinition ToJson(Xml.ModelTableEntry input)
        {
            Json.ModelDefinition output = new();

            output.ModelUri = input.ModelUri;
            output.XmlSchemaUri = input.XmlSchemaUri;
            output.VarVersion = input.Version;
            output.ModelVersion = input.ModelVersion;
            output.PublicationDate = input.PublicationDateSpecified ? input.PublicationDate : null;
            output.DefaultAccessRestrictions = input.AccessRestrictions != 0 ? input.AccessRestrictions : null;
            output.DefaultRolePermissions = ToJsonRolePermission(input.RolePermissions);

            if (input.RequiredModel != null)
            {
                output.RequiredModels = new();

                foreach (var item in input.RequiredModel)
                {
                    output.RequiredModels.Add(new Json.ModelReference()
                    {
                        ModelUri = item.ModelUri,
                        XmlSchemaUri = item.XmlSchemaUri,
                        VarVersion = item.Version,
                        ModelVersion = item.ModelVersion,
                        PublicationDate = item.PublicationDateSpecified ? item.PublicationDate : null
                    });
                }
            }

            return output;
        }

        private Xml.ModelTableEntry ToXml(Json.ModelDefinition input)
        {
            Xml.ModelTableEntry output = new();

            output.ModelUri = input.ModelUri;
            output.XmlSchemaUri = input.XmlSchemaUri;
            output.Version = input.VarVersion;
            output.ModelVersion = input.ModelVersion;
            output.PublicationDate = input.PublicationDate ?? DateTime.MinValue;
            output.PublicationDateSpecified = input.PublicationDate != null;
            output.AccessRestrictions = (ushort)(input.DefaultAccessRestrictions ?? 0);
            output.RolePermissions = ToXmlRolePermission(input.DefaultRolePermissions);

            if (input.RequiredModels != null)
            {
                List<Xml.ModelTableEntry> models = new();

                foreach (var item in input.RequiredModels)
                {
                    models.Add(new Xml.ModelTableEntry()
                    {
                        ModelUri = item.ModelUri,
                        XmlSchemaUri = item.XmlSchemaUri,
                        Version = item.VarVersion,
                        ModelVersion = item.ModelVersion,
                        PublicationDate = item.PublicationDate ?? DateTime.MinValue,
                        PublicationDateSpecified = item.PublicationDate != null
                    });
                }

                output.RequiredModel = models.ToArray();
            }

            return output;
        }


        private void Update(Json.UANode output, Xml.UANode input)
        {
            output.NodeId = ToJsonNodeId(input.NodeId);
            output.SymbolicName = input.SymbolicName;
            output.BrowseName = ToJsonQualifiedName(input.BrowseName);
            output.DisplayName = ToJsonLocalizedText(input.DisplayName);
            output.Description = ToJsonLocalizedText(input.Description);
            output.WriteMask = input.WriteMask != 0 ? input.WriteMask : null;
            output.Documentation = input.Documentation;
            output.ConformationUnits = input.Category != null ? new(input.Category) : null;
            output.ReleaseStatus = ToJsonReleaseStatus(input.ReleaseStatus);
            output.HasNoPermissions = input.HasNoPermissions ? input.HasNoPermissions : null;
            output.RolePermissions = ToJsonRolePermission(input.RolePermissions);
            output.AccessRestrictions = (input.AccessRestrictionsSpecified && input.AccessRestrictions != 0) ? input.AccessRestrictions : null;

            if (input is Xml.UAInstance instance)
            {
                output.ParentId = ToJsonNodeId(instance.ParentNodeId);
            }

            if (input is Xml.UAType type)
            {
                output.IsAbstract = type.IsAbstract ? type.IsAbstract : null;
            }

            QualifiedName qname;

            try
            {
                qname = QualifiedName.Parse(m_context, output.BrowseName!, false);
            }
            catch (Exception)
            {
                qname = output.BrowseName!;
            }

            if (output.SymbolicName == qname.Name)
            {
                output.SymbolicName = null;
            }

            if (output.DisplayName?.T?.Count == 1)
            {
                if (output.DisplayName.T[0].Count >= 2 && output.DisplayName.T[0][1] == qname.Name)
                {
                    output.DisplayName = null;
                }
            }

            if (input.References != null)
            {
                List<Json.Reference> references = new();

                foreach (var reference in input.References)
                {
                    var referenceTypeId = ToJsonNodeId(reference.ReferenceType);

                    if (referenceTypeId == ReferenceTypeIds.HasModellingRule)
                    {
                        output.ModellingRuleId = ToJsonNodeId(reference.Value);
                        continue;
                    }

                    if (referenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                    {
                        output.TypeId = ToJsonNodeId(reference.Value);
                        continue;
                    }

                    references.Add(new Json.Reference()
                    {
                        ReferenceTypeId = referenceTypeId,
                        IsForward = !reference.IsForward ? reference.IsForward : null,
                        TargetId = ToJsonNodeId(reference.Value)
                    });
                }

                output.References = references;
            }
        }

        private void Update(Xml.UANode output, Json.UANode input)
        {
            output.NodeId = ToXmlNodeId(input.NodeId, true);
            output.SymbolicName = input.SymbolicName;
            output.BrowseName = ToXmlQualifiedName(input.BrowseName);
            output.DisplayName = ToXmlLocalizedText(input.DisplayName);
            output.Description = ToXmlLocalizedText(input.Description);
            output.WriteMask = (uint)(input.WriteMask ?? 0);
            output.Documentation = input.Documentation;
            output.Category = input.ConformationUnits != null ? input.ConformationUnits.ToArray() : null;
            output.ReleaseStatus = ToXmlReleaseStatus(input.ReleaseStatus);
            output.HasNoPermissions = input.HasNoPermissions ?? false;
            output.RolePermissions = ToXmlRolePermission(input.RolePermissions);
            output.AccessRestrictions = (ushort)(input.AccessRestrictions ?? 0);
            output.AccessRestrictionsSpecified = input.AccessRestrictions != null && input.AccessRestrictions != 0;

            if (output is Xml.UAInstance instance)
            {
                instance.ParentNodeId = ToXmlNodeId(input.ParentId);
            }

            if (output is Xml.UAType type)
            {
                type.IsAbstract = input.IsAbstract ?? false;
            }

            QualifiedName qname;

            try
            {
                qname = QualifiedName.Parse(m_context, output.BrowseName!, false);
            }
            catch (Exception)
            {
                qname = output.BrowseName!;
            }

            if (output.SymbolicName == qname.Name)
            {
                output.SymbolicName = null;
            }

            if (output.DisplayName?.Length == 1)
            {
                if (output.DisplayName[0].Value == qname.Name)
                {
                    output.DisplayName = null;
                }
            }

            List<Xml.Reference> references = new();

            if (!String.IsNullOrEmpty(input.ModellingRuleId))
            {
                references.Add(new Xml.Reference()
                {
                    ReferenceType = ReferenceTypeIds.HasModellingRule,
                    IsForward = true,
                    Value = ToXmlNodeId(input.ModellingRuleId)
                });
            }

            if (!String.IsNullOrEmpty(input.TypeId))
            {
                references.Add(new Xml.Reference()
                {
                    ReferenceType = ReferenceTypeIds.HasTypeDefinition,
                    IsForward = true,
                    Value = ToXmlNodeId(input.TypeId)
                });
            }

            if (input.References != null)
            {
                foreach (var reference in input.References)
                {
                    references.Add(new Xml.Reference()
                    {
                        ReferenceType = ToXmlNodeId(reference.ReferenceTypeId),
                        IsForward = reference.IsForward ?? true,
                        Value = ToXmlNodeId(reference.TargetId)
                    });
                }
            }

            if (references.Count > 0)
            {
                output.References = references.ToArray();
            }
        }

        private Json.UAObjectType ToJsonNode(Xml.UAObjectType input)
        {
            var output = new Json.UAObjectType();
            output.NodeClass = Json.NodeClass.UAObjectType;
            Update(output, input);
            return output;
        }

        private Xml.UAObjectType ToXmlNode(Json.UAObjectType input)
        {
            var output = new Xml.UAObjectType();
            Update(output, input);
            return output;
        }

        private Json.UAVariableType ToJsonNode(Xml.UAVariableType input)
        {
            var output = new Json.UAVariableType();
            output.NodeClass = Json.NodeClass.UAVariableType;
            Update(output, input);
            output.DataType = ToJsonNodeId(input.DataType);
            output.ValueRank = input.ValueRank != ValueRanks.Scalar ? input.ValueRank : null;
            output.ArrayDimensions = !String.IsNullOrEmpty(input.ArrayDimensions) ? input.ArrayDimensions : null;
            output.Value = ToJsonVariant(input.Value);
            return output;
        }

        private Xml.UAVariableType ToXmlNode(Json.UAVariableType input)
        {
            var output = new Xml.UAVariableType();
            Update(output, input);
            output.DataType = ToXmlNodeId(input.DataType) ?? "i=24";
            output.ValueRank = (int)(input.ValueRank ?? ValueRanks.Scalar);
            output.ArrayDimensions = !String.IsNullOrEmpty(input.ArrayDimensions) ? input.ArrayDimensions : null;
            output.Value = ToXmlVariant(input.Value);
            return output;
        }

        private Json.UADataType ToJsonNode(Xml.UADataType input)
        {
            var output = new Json.UADataType();
            output.NodeClass = Json.NodeClass.UADataType;
            Update(output, input);
            output.Purpose = ToJsonDataTypePurpose(input.Purpose);
            output.Definition = ToJsonDataTypeDefinition(input.Definition);
            return output;
        }

        private Xml.UADataType ToXmlNode(Json.UADataType input)
        {
            var output = new Xml.UADataType();
            Update(output, input);
            output.Purpose = ToXmlDataTypePurpose(input.Purpose);
            output.Definition = ToXmlDataTypeDefinition(input.Definition);
            return output;
        }

        private Json.UAReferenceType ToJsonNode(Xml.UAReferenceType input)
        {
            var output = new Json.UAReferenceType();
            output.NodeClass = Json.NodeClass.UAReferenceType;
            Update(output, input);
            output.InverseName = ToJsonLocalizedText(input.InverseName);
            output.Symmetric = !input.Symmetric ? input.Symmetric : null;
            return output;
        }

        private Xml.UAReferenceType ToXmlNode(Json.UAReferenceType input)
        {
            var output = new Xml.UAReferenceType();
            Update(output, input);
            output.InverseName = ToXmlLocalizedText(input.InverseName);
            output.Symmetric = input.Symmetric ?? true;
            return output;
        }

        private Json.UAObject ToJsonNode(Xml.UAObject input)
        {
            var output = new Json.UAObject();
            output.NodeClass = Json.NodeClass.UAObject;
            Update(output, input);
            output.EventNotifier = input.EventNotifier != EventNotifiers.None ? input.EventNotifier : null;
            return output;
        }

        private Xml.UAObject ToXmlNode(Json.UAObject input)
        {
            var output = new Xml.UAObject();
            Update(output, input);
            output.EventNotifier = (byte)(input.EventNotifier ?? EventNotifiers.None);
            return output;
        }

        private Json.UAVariable ToJsonNode(Xml.UAVariable input)
        {
            var output = new Json.UAVariable();
            output.NodeClass = Json.NodeClass.UAVariable;
            Update(output, input);
            output.DataType = ToJsonNodeId(input.DataType);
            output.ValueRank = input.ValueRank != ValueRanks.Scalar ? input.ValueRank : null;
            output.ArrayDimensions = !String.IsNullOrEmpty(input.ArrayDimensions) ? input.ArrayDimensions : null;
            output.Historizing = input.Historizing ? input.Historizing : null;
            output.MinimumSamplingInterval = input.MinimumSamplingInterval != 0 ? (decimal)input.MinimumSamplingInterval : null;
            output.Value = ToJsonVariant(input.Value);
            return output;
        }

        private Xml.UAVariable ToXmlNode(Json.UAVariable input)
        {
            var output = new Xml.UAVariable();
            Update(output, input);
            output.DataType = ToXmlNodeId(input.DataType) ?? "i=24";
            output.ValueRank = input.ValueRank ?? ValueRanks.Scalar;
            output.ArrayDimensions = !String.IsNullOrEmpty(input.ArrayDimensions) ? input.ArrayDimensions : null;
            output.Historizing = input.Historizing ?? false;
            output.MinimumSamplingInterval = (double)(input.MinimumSamplingInterval ?? 0);
            output.Value = ToXmlVariant(input.Value);
            return output;
        }

        private Json.UAMethod ToJsonNode(Xml.UAMethod input)
        {
            var output = new Json.UAMethod();
            output.NodeClass = Json.NodeClass.UAMethod;
            Update(output, input);
            output.Executable = !input.Executable ? input.Executable : null;
            return output;
        }

        private Xml.UAMethod ToXmlNode(Json.UAMethod input)
        {
            var output = new Xml.UAMethod();
            Update(output, input);
            output.Executable = input.Executable ?? true;
            return output;
        }

        private Json.UAView ToJsonNode(Xml.UAView input)
        {
            var output = new Json.UAView();
            output.NodeClass = Json.NodeClass.UAView;
            Update(output, input);
            output.EventNotifier = input.EventNotifier != EventNotifiers.None ? input.EventNotifier : null;
            output.ContainsNoLoops = input.ContainsNoLoops ? input.ContainsNoLoops : null;
            return output;
        }

        private Xml.UAView ToXmlNode(Json.UAView input)
        {
            var output = new Xml.UAView();
            Update(output, input);
            output.EventNotifier = (byte)(input.EventNotifier ?? EventNotifiers.None);
            output.ContainsNoLoops = input.ContainsNoLoops ?? false;
            return output;
        }

        private Json.Variant? ToJsonVariant(XmlElement? input)
        {
            if (input == null)
            {
                return null;
            }

            // TODO: implement XML-to-JSON variant conversion without Opc.Ua.Core dependency.
            return null;
        }

        private XmlElement? ToXmlVariant(Json.Variant? input)
        {
            if (input == null)
            {
                return null;
            }
;
            return null;
        }

        private string? ToJsonNodeId(string? input)
        {
            if (String.IsNullOrEmpty(input))
            {
                return null;
            }

            try
            {
                if (m_aliases!.TryGetValue(input, out var value))
                {
                    input = value;
                }

                var nid = Opc.Ua.ExpandedNodeId.Parse(m_context, input);
                return nid.Format(m_context, true);
            }
            catch (Exception)
            {
                return "s=" + input;
            }
        }

        private string? ToXmlNodeId(string? input, bool noAlias = false)
        {
            if (String.IsNullOrEmpty(input))
            {
                return null;
            }

            try
            {
                var nid = Opc.Ua.ExpandedNodeId.Parse(m_context, input);
                var text = nid.Format(m_context, false);

                if (!noAlias)
                {
                    var alias = m_aliases!.Where(x => x.Value == text).Select(x => x.Key).FirstOrDefault();

                    if (alias != null)
                    {
                        return alias;
                    }
                }

                return text;

            }
            catch (Exception)
            {
                return "s=" + input;
            }
        }

        private string? ToJsonQualifiedName(string? input)
        {
            if (String.IsNullOrEmpty(input))
            {
                return null;
            }

            try
            {
                var qn = Opc.Ua.QualifiedName.Parse(m_context, input, false);
                return qn.Format(m_context, true);
            }
            catch (Exception)
            {
                return input;
            }
        }

        private string? ToXmlQualifiedName(string? input)
        {
            if (String.IsNullOrEmpty(input))
            {
                return null;
            }

            try
            {
                var qn = Opc.Ua.QualifiedName.Parse(m_context, input, false);
                return qn.Format(m_context, false);
            }
            catch (Exception)
            {
                return input;
            }
        }

        private static Json.LocalizedText? ToJsonLocalizedText(IList<Xml.LocalizedText>? input)
        {
            if (input == null || input.Count == 0)
            {
                return null;
            }

            Json.LocalizedText output = new();
            output.T = new List<List<string>>();

            foreach (var item in input)
            {
                output.T.Add([item.Locale ?? "", item.Value ?? ""]);
            }

            return output;
        }


        private static Xml.LocalizedText[]? ToXmlLocalizedText(Json.LocalizedText? input)
        {
            if (input?.T == null)
            {
                return null;
            }

            List<Xml.LocalizedText> output = new();

            foreach (var item in input.T)
            {
                if (item.Count > 1)
                {
                    output.Add(new Xml.LocalizedText() { Locale = item[0], Value = item[1] });
                }
            }

            return output.ToArray();
        }

        private List<Json.RolePermission>? ToJsonRolePermission(IList<Xml.RolePermission>? input)
        {
            if (input == null || input.Count == 0)
            {
                return null;
            }

            List<Json.RolePermission> output = new();

            foreach (var item in input)
            {
                output.Add(new Json.RolePermission(ToJsonNodeId(item.Value), item.Permissions));
            }

            return output;
        }

        private Xml.RolePermission[]? ToXmlRolePermission(IList<Json.RolePermission>? input)
        {
            if (input == null || input.Count == 0)
            {
                return null;
            }

            List<Xml.RolePermission> output = new();

            foreach (var item in input)
            {
                output.Add(new Xml.RolePermission() { Permissions = (uint)(item.Permissions ?? 0), Value = ToXmlNodeId(item.RoleId) });
            }

            if (output.Count > 0)
            {
                return output.ToArray();
            }

            return null;
        }

        private static Json.ReleaseStatus? ToJsonReleaseStatus(Xml.ReleaseStatus input)
        {
            switch (input)
            {
                case Xml.ReleaseStatus.Draft: return Json.ReleaseStatus.Draft;
                case Xml.ReleaseStatus.Deprecated: return Json.ReleaseStatus.Deprecated;
            }

            return null;
        }

        private static Xml.ReleaseStatus ToXmlReleaseStatus(Json.ReleaseStatus? input)
        {
            if (input != null)
            {
                switch (input)
                {
                    case Json.ReleaseStatus.Draft: return Xml.ReleaseStatus.Draft;
                    case Json.ReleaseStatus.Deprecated: return Xml.ReleaseStatus.Deprecated;
                }
            }

            return Xml.ReleaseStatus.Released;
        }

        private static Json.DataTypePurpose? ToJsonDataTypePurpose(Xml.DataTypePurpose input)
        {
            switch (input)
            {
                case Xml.DataTypePurpose.CodeGenerator: return Json.DataTypePurpose.CodeGenerator;
                case Xml.DataTypePurpose.ServicesOnly: return Json.DataTypePurpose.ServicesOnly;
            }

            return null;
        }

        private static Xml.DataTypePurpose ToXmlDataTypePurpose(Json.DataTypePurpose? input)
        {
            if (input != null)
            {
                switch (input)
                {
                    case Json.DataTypePurpose.CodeGenerator: return Xml.DataTypePurpose.CodeGenerator;
                    case Json.DataTypePurpose.ServicesOnly: return Xml.DataTypePurpose.ServicesOnly;
                }
            }

            return Xml.DataTypePurpose.Normal;
        }

        private Json.DataTypeDefinition? ToJsonDataTypeDefinition(Xml.DataTypeDefinition? input)
        {
            if (input == null)
            {
                return null;
            }

            var output = new Json.DataTypeDefinition();

            output.Name = input.Name;
            output.SymbolicName = input.SymbolicName;
            output.IsOptionSet = (input.IsOptionSet) ? input.IsOptionSet : null;
            output.IsUnion = (input.IsUnion) ? input.IsUnion : null;
            output.Fields = new();

            if (input.Field != null)
            {
                foreach (var field in input.Field)
                {
                    output.Fields.Add(new Json.DataTypeField()
                    {
                        Name = field.Name,
                        SymbolicName = field.SymbolicName,
                        DataType = ToJsonNodeId(field.DataType),
                        ValueRank = field.ValueRank != ValueRanks.Scalar ? field.ValueRank : null,
                        ArrayDimensions = !String.IsNullOrEmpty(field.ArrayDimensions) ? field.ArrayDimensions : null,
                        Description = ToJsonLocalizedText(field.Description),
                        DisplayName = ToJsonLocalizedText(field.DisplayName),
                        IsOptional = (field.IsOptional) ? field.IsOptional : null,
                        AllowSubTypes = (field.AllowSubTypes) ? field.AllowSubTypes : null,
                        MaxStringLength = (field.MaxStringLength > 0) ? (int)field.MaxStringLength : null,
                        Value = field.Value != 0 ? field.Value : null
                    });
                }
            }

            return output;
        }

        private Xml.DataTypeDefinition? ToXmlDataTypeDefinition(Json.DataTypeDefinition? input)
        {
            if (input == null)
            {
                return null;
            }

            var output = new Xml.DataTypeDefinition();

            output.Name = input.Name;
            output.SymbolicName = input.SymbolicName;
            output.IsOptionSet = input.IsOptionSet ?? false;
            output.IsUnion = input.IsUnion ?? false;

            List<Xml.DataTypeField> fields = new();

            if (input.Fields != null)
            {
                foreach (var field in input.Fields)
                {
                    fields.Add(new Xml.DataTypeField()
                    {
                        Name = field.Name,
                        SymbolicName = field.SymbolicName,
                        DataType = ToXmlNodeId(field.DataType) ?? "i=24",
                        ValueRank = field.ValueRank ?? ValueRanks.Scalar,
                        ArrayDimensions = !String.IsNullOrEmpty(field.ArrayDimensions) ? field.ArrayDimensions : null,
                        Description = ToXmlLocalizedText(field.Description),
                        DisplayName = ToXmlLocalizedText(field.DisplayName),
                        IsOptional = field.IsOptional ?? false,
                        AllowSubTypes = field.AllowSubTypes ?? false,
                        MaxStringLength = (uint)(field.MaxStringLength ?? 0),
                        Value = field.Value ?? 0
                    });
                }
            }

            output.Field = fields.ToArray();

            return output;
        }

        public void LoadInto(AddressSpace addressSpace)
        {
            // Validate that all external node IDs from JSON-LD exist in the address space
            if (m_externalNodeIds != null && m_externalNodeIds.Count > 0)
            {
                var missing = new List<string>();
                foreach (var extId in m_externalNodeIds)
                {
                    if (addressSpace.Read(extId) == null)
                        missing.Add(extId);
                }
                if (missing.Count > 0)
                    throw new InvalidOperationException(
                        $"External node(s) not found in AddressSpace: {string.Join(", ", missing)}");
            }

            addressSpace.AddNodeSet(BuildJson());
        }

        public static NodeSetSerializer FromAddressSpace(AddressSpace addressSpace, string modelUri)
        {
            var serializer = new NodeSetSerializer();
            var nodeSet = addressSpace.GetNodeSet(modelUri);
            serializer.LoadFromNodeSet(nodeSet);
            return serializer;
        }

        public static NodeSetSerializer FromAddressSpaceAsJsonLd(AddressSpace addressSpace, string modelUri)
        {
            var serializer = new NodeSetSerializer();
            var (nodeSet, stubs) = addressSpace.GetNodeSetWithStubs(modelUri);
            serializer.LoadFromNodeSet(nodeSet);
            serializer.m_stubs = stubs;
            return serializer;
        }

        private void LoadFromNodeSet(Json.UANodeSet nodeSet)
        {
            m_context = new ServiceMessageContext();
            LoadWellKnownAliases();

            m_models = new();

            if (nodeSet.Models != null)
            {
                foreach (var model in nodeSet.Models)
                {
                    m_context.NamespaceUris.GetIndexOrAppend(model.ModelUri!);

                    if (model.RequiredModels != null)
                    {
                        foreach (var dep in model.RequiredModels)
                            m_context.NamespaceUris.GetIndexOrAppend(dep.ModelUri!);
                    }

                    m_models[model.ModelUri!] = model;
                }
            }

            m_nodes = new();
            m_sequence = new();

            if (nodeSet.Nodes != null)
            {
                void IndexList(IEnumerable<Json.UANode>? nodes)
                {
                    if (nodes == null) return;
                    foreach (var node in nodes)
                    {
                        if (m_nodes.ContainsKey(node.NodeId!)) continue; // already indexed via parent's Children
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                IndexList(nodeSet.Nodes.N1ReferenceTypes);
                IndexList(nodeSet.Nodes.N2DataTypes);
                IndexList(nodeSet.Nodes.N3VariableTypes);
                IndexList(nodeSet.Nodes.N4ObjectTypes);
                IndexList(nodeSet.Nodes.N5Variables);
                IndexList(nodeSet.Nodes.N6Methods);
                IndexList(nodeSet.Nodes.N7Objects);
                IndexList(nodeSet.Nodes.N8Views);
            }

            // Discover and register any namespace URIs referenced by nodes that
            // are not already in the context (e.g. cross-model TypeDefinition refs).
            foreach (var node in m_sequence!)
            {
                RegisterNsuUri(node.NodeId);
                RegisterNsuUri(node.BrowseName);
                RegisterNsuUri(node.ParentId);
                RegisterNsuUri(node.TypeId);
                RegisterNsuUri(node.ModellingRuleId);

                if (node is Json.UAVariable v)
                    RegisterNsuUri(v.DataType);
                if (node is Json.UAVariableType vt)
                    RegisterNsuUri(vt.DataType);
                if (node is Json.UADataType dt && dt.Definition?.Fields != null)
                {
                    foreach (var field in dt.Definition.Fields)
                        RegisterNsuUri(field.DataType);
                }

                if (node.References != null)
                {
                    foreach (var r in node.References)
                    {
                        RegisterNsuUri(r.ReferenceTypeId);
                        RegisterNsuUri(r.TargetId);
                    }
                }
            }
        }

        private void RegisterNsuUri(string? value)
        {
            if (value != null && value.StartsWith("nsu="))
            {
                var semi = value.IndexOf(';');
                if (semi > 4)
                {
                    var uri = value.Substring(4, semi - 4);
                    m_context!.NamespaceUris.GetIndexOrAppend(uri);
                }
            }
        }

        #region JSON-LD Serialization

        public void SaveJsonLd(string filePath)
        {
            using var stream = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite);
            SaveJsonLd(stream);
        }

        public void SaveJsonLd(Stream stream)
        {
            var doc = BuildJsonLd();
            using var writer = new StreamWriter(stream, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(writer) { Formatting = Newtonsoft.Json.Formatting.Indented };
            doc.WriteTo(jsonWriter);
        }

        public void LoadJsonLd(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var doc = JObject.Parse(json);

            // Parse @context to build prefix → URI mappings
            var context = doc["@context"] as JObject;
            var prefixToUri = new Dictionary<string, string>();
            if (context != null)
            {
                foreach (var prop in context.Properties())
                {
                    if (prop.Value.Type == JTokenType.String)
                    {
                        var value = prop.Value.ToString();
                        if (value.Contains("://") || value.StartsWith("urn:"))
                            prefixToUri[prop.Name] = value;
                    }
                }
            }

            // Parse model metadata from root level (named graph format)
            var modelDef = new Json.ModelDefinition();
            modelDef.ModelUri = doc["modelUri"]?.ToString();
            modelDef.XmlSchemaUri = doc["xmlSchemaUri"]?.ToString();
            modelDef.VarVersion = doc["version"]?.ToString();
            modelDef.ModelVersion = doc["modelVersion"]?.ToString();

            var pubDate = doc["publicationDate"]?.ToString();
            if (pubDate != null && DateTime.TryParse(pubDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDate))
                modelDef.PublicationDate = parsedDate;

            var reqModels = doc["requiredModels"] as JArray;
            if (reqModels != null)
            {
                modelDef.RequiredModels = new List<Json.ModelReference>();
                foreach (var req in reqModels)
                {
                    var reqRef = new Json.ModelReference();
                    reqRef.ModelUri = req["modelUri"]?.ToString();
                    reqRef.XmlSchemaUri = req["xmlSchemaUri"]?.ToString();
                    reqRef.VarVersion = req["version"]?.ToString();
                    var reqPubDate = req["publicationDate"]?.ToString();
                    if (reqPubDate != null && DateTime.TryParse(reqPubDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var rpd))
                        reqRef.PublicationDate = rpd;
                    modelDef.RequiredModels.Add(reqRef);
                }
            }

            // Parse @graph — collect externals, load normative nodes
            var graph = doc["@graph"] as JArray;
            var allNodes = new List<Json.UANode>();
            var externalIds = new List<string>();

            if (graph != null)
            {
                foreach (var entry in graph)
                {
                    if (entry is not JObject nodeObj) continue;

                    // Support both new (isExternal) and legacy (isExternalReference) property names
                    if (nodeObj["isExternal"]?.Value<bool>() == true
                        || nodeObj["isExternalReference"]?.Value<bool>() == true)
                    {
                        var extId = nodeObj["@id"]?.ToString();
                        if (extId != null)
                            externalIds.Add(FromCurie(extId, prefixToUri));
                        continue;
                    }

                    var node = JsonLdEntryToNode(nodeObj, prefixToUri);
                    if (node != null)
                        allNodes.Add(node);
                }
            }

            m_externalNodeIds = externalIds;

            // Build Children hierarchy from flat list
            var nodeDict = new Dictionary<string, Json.UANode>();
            foreach (var n in allNodes)
                if (n.NodeId != null)
                    nodeDict[n.NodeId] = n;

            foreach (var n in allNodes)
            {
                if (n.ParentId != null && nodeDict.TryGetValue(n.ParentId, out var parent))
                {
                    parent.Children ??= new Json.ChildList();
                    switch (n)
                    {
                        case Json.UAObject od: (parent.Children.Objects ??= new()).Add(od); break;
                        case Json.UAVariable vn: (parent.Children.Variables ??= new()).Add(vn); break;
                        case Json.UAMethod mn: (parent.Children.Methods ??= new()).Add(mn); break;
                    }
                }
            }

            // Build UANodeSet with only top-level nodes
            var nodeSet = new Json.UANodeSet();
            nodeSet.Models = new List<Json.ModelDefinition> { modelDef };
            nodeSet.Nodes = new Json.UANodeSetNodes();

            foreach (var n in allNodes)
            {
                // Skip children — they're nested under their parent
                if (n.ParentId != null && nodeDict.ContainsKey(n.ParentId))
                    continue;

                switch (n)
                {
                    case Json.UAReferenceType rt: (nodeSet.Nodes.N1ReferenceTypes ??= new()).Add(rt); break;
                    case Json.UADataType dt: (nodeSet.Nodes.N2DataTypes ??= new()).Add(dt); break;
                    case Json.UAVariableType vt: (nodeSet.Nodes.N3VariableTypes ??= new()).Add(vt); break;
                    case Json.UAObjectType ot: (nodeSet.Nodes.N4ObjectTypes ??= new()).Add(ot); break;
                    case Json.UAVariable vn: (nodeSet.Nodes.N5Variables ??= new()).Add(vn); break;
                    case Json.UAMethod mn: (nodeSet.Nodes.N6Methods ??= new()).Add(mn); break;
                    case Json.UAObject on: (nodeSet.Nodes.N7Objects ??= new()).Add(on); break;
                    case Json.UAView wn: (nodeSet.Nodes.N8Views ??= new()).Add(wn); break;
                }
            }

            LoadWellKnownAliases();
            IndexFile(nodeSet);
        }

        private JObject BuildJsonLd()
        {
            var doc = new JObject();
            var context = BuildJsonLdContext();
            doc["@context"] = context;

            // Model metadata at root creates a named graph — optimal for SPARQL
            // (SELECT ... FROM <modelUri> WHERE { ... })
            var model = m_models!.Values.FirstOrDefault();
            if (model != null)
            {
                doc["@id"] = model.ModelUri;
                doc["@type"] = "opcua:UANodeSet";
                doc["modelUri"] = model.ModelUri;
                if (model.XmlSchemaUri != null) doc["xmlSchemaUri"] = model.XmlSchemaUri;
                if (model.VarVersion != null) doc["version"] = model.VarVersion;
                if (model.ModelVersion != null) doc["modelVersion"] = model.ModelVersion;
                if (model.PublicationDate != null)
                    doc["publicationDate"] = model.PublicationDate.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                if (model.RequiredModels != null && model.RequiredModels.Count > 0)
                {
                    var reqArray = new JArray();
                    foreach (var req in model.RequiredModels)
                    {
                        var reqObj = new JObject();
                        reqObj["modelUri"] = req.ModelUri;
                        if (req.XmlSchemaUri != null) reqObj["xmlSchemaUri"] = req.XmlSchemaUri;
                        if (req.VarVersion != null) reqObj["version"] = req.VarVersion;
                        if (req.PublicationDate != null)
                            reqObj["publicationDate"] = req.PublicationDate.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                        reqArray.Add(reqObj);
                    }
                    doc["requiredModels"] = reqArray;
                }
            }

            var graph = new JArray();
            foreach (var node in m_sequence!)
                graph.Add(NodeToJsonLd(node, isStub: false));

            if (m_stubs != null)
                foreach (var stub in m_stubs)
                    graph.Add(NodeToJsonLd(stub, isStub: true));

            doc["@graph"] = graph;
            return doc;
        }

        private JObject BuildJsonLdContext()
        {
            m_nsPrefixes = new Dictionary<string, string>();
            var context = new JObject();

            // Core namespace
            m_nsPrefixes["http://opcfoundation.org/UA/"] = "opcua";
            context["opcua"] = "http://opcfoundation.org/UA/";

            // Model namespaces and their required models
            foreach (var model in m_models!.Values)
            {
                RegisterNamespacePrefix(model.ModelUri!, context);

                if (model.RequiredModels != null)
                    foreach (var req in model.RequiredModels)
                        if (req.ModelUri != null)
                            RegisterNamespacePrefix(req.ModelUri, context);
            }

            context["xsd"] = "http://www.w3.org/2001/XMLSchema#";
            context["rdfs"] = "http://www.w3.org/2000/01/rdf-schema#";

            // Property mappings
            context["browseName"] = "opcua:BrowseName";
            context["symbolicName"] = "opcua:SymbolicName";
            context["description"] = "rdfs:comment";
            context["releaseStatus"] = "opcua:ReleaseStatus";
            context["valueRank"] = new JObject { ["@id"] = "opcua:ValueRank", ["@type"] = "xsd:integer" };
            context["arrayDimensions"] = "opcua:ArrayDimensions";
            context["accessRestrictions"] = new JObject { ["@id"] = "opcua:AccessRestrictions", ["@type"] = "xsd:integer" };
            context["isUnion"] = new JObject { ["@id"] = "opcua:IsUnion", ["@type"] = "xsd:boolean" };
            context["isOptional"] = new JObject { ["@id"] = "opcua:IsOptional", ["@type"] = "xsd:boolean" };
            context["allowSubTypes"] = new JObject { ["@id"] = "opcua:AllowSubTypes", ["@type"] = "xsd:boolean" };

            // Implicit reference properties
            context["dataType"] = new JObject { ["@id"] = "opcua:HasDataType", ["@type"] = "@id" };
            context["typeDefinition"] = new JObject { ["@id"] = "opcua:HasTypeDefinition", ["@type"] = "@id" };
            context["parent"] = new JObject { ["@id"] = "opcua:HasParent", ["@type"] = "@id" };
            context["modellingRule"] = new JObject { ["@id"] = "opcua:HasModellingRule", ["@type"] = "@id" };

            // Forward reference properties
            context["HasSubtype"] = new JObject { ["@id"] = "opcua:HasSubtype", ["@type"] = "@id" };
            context["HasProperty"] = new JObject { ["@id"] = "opcua:HasProperty", ["@type"] = "@id" };
            context["HasComponent"] = new JObject { ["@id"] = "opcua:HasComponent", ["@type"] = "@id" };
            context["Organizes"] = new JObject { ["@id"] = "opcua:Organizes", ["@type"] = "@id" };
            context["HasEncoding"] = new JObject { ["@id"] = "opcua:HasEncoding", ["@type"] = "@id" };
            context["HasDescription"] = new JObject { ["@id"] = "opcua:HasDescription", ["@type"] = "@id" };
            context["HasOrderedComponent"] = new JObject { ["@id"] = "opcua:HasOrderedComponent", ["@type"] = "@id" };

            // Inverse reference properties
            context["SubtypeOf"] = new JObject { ["@id"] = "opcua:SubtypeOf", ["@type"] = "@id" };
            context["PropertyOf"] = new JObject { ["@id"] = "opcua:PropertyOf", ["@type"] = "@id" };
            context["ComponentOf"] = new JObject { ["@id"] = "opcua:ComponentOf", ["@type"] = "@id" };
            context["OrganizedBy"] = new JObject { ["@id"] = "opcua:OrganizedBy", ["@type"] = "@id" };
            context["EncodingOf"] = new JObject { ["@id"] = "opcua:EncodingOf", ["@type"] = "@id" };
            context["OrderedComponentOf"] = new JObject { ["@id"] = "opcua:OrderedComponentOf", ["@type"] = "@id" };

            // DataType definition
            context["definition"] = "opcua:HasDefinition";
            context["fields"] = "opcua:HasField";
            context["fieldName"] = "opcua:FieldName";
            context["fieldValue"] = new JObject { ["@id"] = "opcua:FieldValue", ["@type"] = "xsd:integer" };
            context["fieldDataType"] = new JObject { ["@id"] = "opcua:FieldDataType", ["@type"] = "@id" };

            // Other
            context["rolePermissions"] = "opcua:RolePermissions";
            context["roleId"] = new JObject { ["@id"] = "opcua:RoleId", ["@type"] = "@id" };
            context["permissions"] = new JObject { ["@id"] = "opcua:Permissions", ["@type"] = "xsd:integer" };
            context["modelUri"] = new JObject { ["@id"] = "opcua:ModelUri", ["@type"] = "@id" };
            context["version"] = "opcua:Version";
            context["modelVersion"] = "opcua:ModelVersion";
            context["publicationDate"] = new JObject { ["@id"] = "opcua:PublicationDate", ["@type"] = "xsd:dateTime" };
            context["xmlSchemaUri"] = new JObject { ["@id"] = "opcua:XmlSchemaUri", ["@type"] = "@id" };
            context["requiredModels"] = "opcua:RequiredModels";
            context["isExternal"] = new JObject { ["@id"] = "opcua:IsExternal", ["@type"] = "xsd:boolean" };
            context["isNormative"] = new JObject { ["@id"] = "opcua:IsNormative", ["@type"] = "xsd:boolean" };
            context["references"] = "opcua:References";
            context["referenceType"] = new JObject { ["@id"] = "opcua:ReferenceType", ["@type"] = "@id" };
            context["target"] = new JObject { ["@id"] = "opcua:Target", ["@type"] = "@id" };

            return context;
        }

        private void RegisterNamespacePrefix(string nsUri, JObject context)
        {
            if (m_nsPrefixes!.ContainsKey(nsUri)) return;
            if (nsUri == "http://opcfoundation.org/UA/") return;

            var prefix = DerivePrefix(nsUri);
            while (m_nsPrefixes.ContainsValue(prefix))
                prefix += "2";

            m_nsPrefixes[nsUri] = prefix;
            var contextUri = nsUri;
            if (!contextUri.EndsWith("/") && !contextUri.EndsWith("#"))
                contextUri += "/";
            context[prefix] = contextUri;
        }

        private static string DerivePrefix(string nsUri)
        {
            // Extract last segment from URI
            var lastSeg = nsUri.TrimEnd('/');
            int pos = Math.Max(lastSeg.LastIndexOf('/'), Math.Max(lastSeg.LastIndexOf(':'), lastSeg.LastIndexOf('#')));
            if (pos >= 0 && pos < lastSeg.Length - 1)
                lastSeg = lastSeg.Substring(pos + 1);

            // Take PascalCase initials
            var initials = new System.Text.StringBuilder();
            foreach (char c in lastSeg)
            {
                if (char.IsUpper(c))
                    initials.Append(char.ToLower(c));
            }

            if (initials.Length >= 2)
                return initials.ToString();

            return lastSeg.Substring(0, Math.Min(4, lastSeg.Length)).ToLower();
        }

        private string ToCurie(string? nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return "";

            if (nodeId.StartsWith("nsu="))
            {
                int semi = nodeId.IndexOf(';', 4);
                if (semi >= 0)
                {
                    var nsUri = nodeId.Substring(4, semi - 4);
                    var idPart = nodeId.Substring(semi + 1);

                    if (m_nsPrefixes != null && m_nsPrefixes.TryGetValue(nsUri, out var prefix))
                        return $"{prefix}:{idPart}";
                }
            }

            // No nsu= prefix — core namespace (ns=0)
            return $"opcua:{nodeId}";
        }

        private static string FromCurie(string curie, Dictionary<string, string> prefixToUri)
        {
            int colon = curie.IndexOf(':');
            if (colon < 0) return curie;

            var prefix = curie.Substring(0, colon);
            var localPart = curie.Substring(colon + 1);

            if (prefixToUri.TryGetValue(prefix, out var nsUri))
            {
                // Core namespace — bare node id
                var cleanUri = nsUri.TrimEnd('/');
                if (cleanUri == "http://opcfoundation.org/UA")
                    return localPart;

                return $"nsu={cleanUri};{localPart}";
            }

            return curie;
        }

        private JObject NodeToJsonLd(Json.UANode node, bool isStub)
        {
            var obj = new JObject();
            obj["@id"] = ToCurie(node.NodeId);
            obj["@type"] = NodeClassToJsonLdType(node);

            if (node.BrowseName != null)
                obj["browseName"] = node.BrowseName;

            if (isStub)
            {
                // Stubs: only emit SubtypeOf + isExternal
                if (node.References != null)
                {
                    foreach (var r in node.References)
                    {
                        if (r.ReferenceTypeId == ReferenceTypeIds.HasSubtype && !(r.IsForward ?? true))
                            obj["SubtypeOf"] = ToCurie(r.TargetId);
                    }
                }
                obj["isExternal"] = true;
                return obj;
            }

            obj["isNormative"] = true;

            if (node.SymbolicName != null)
                obj["symbolicName"] = node.SymbolicName;

            if (node.Description?.T != null && node.Description.T.Count > 0)
            {
                var desc = node.Description.T[0];
                if (desc.Count >= 2 && !string.IsNullOrEmpty(desc[1]))
                    obj["description"] = desc[1];
            }

            if (node.ReleaseStatus != null)
                obj["releaseStatus"] = node.ReleaseStatus.ToString();

            // dataType
            if (node is Json.UAVariable v && v.DataType != null) obj["dataType"] = ToCurie(v.DataType);
            if (node is Json.UAVariableType vt && vt.DataType != null) obj["dataType"] = ToCurie(vt.DataType);

            // valueRank
            if (node is Json.UAVariable v2 && v2.ValueRank != null) obj["valueRank"] = v2.ValueRank;
            if (node is Json.UAVariableType vt2 && vt2.ValueRank != null) obj["valueRank"] = vt2.ValueRank;

            // arrayDimensions
            if (node is Json.UAVariable v3 && v3.ArrayDimensions != null) obj["arrayDimensions"] = v3.ArrayDimensions;
            if (node is Json.UAVariableType vt3 && vt3.ArrayDimensions != null) obj["arrayDimensions"] = vt3.ArrayDimensions;

            if (node.TypeId != null) obj["typeDefinition"] = ToCurie(node.TypeId);
            if (node.ModellingRuleId != null) obj["modellingRule"] = ToCurie(node.ModellingRuleId);
            if (node.ParentId != null) obj["parent"] = ToCurie(node.ParentId);

            if (node.AccessRestrictions != null && node.AccessRestrictions != 0)
                obj["accessRestrictions"] = node.AccessRestrictions;

            // rolePermissions
            if (node.RolePermissions != null && node.RolePermissions.Count > 0)
            {
                var rpArray = new JArray();
                foreach (var rp in node.RolePermissions)
                {
                    var rpObj = new JObject();
                    rpObj["roleId"] = ToCurie(rp.RoleId);
                    rpObj["permissions"] = rp.Permissions;
                    rpArray.Add(rpObj);
                }
                obj["rolePermissions"] = rpArray;
            }

            // Convert references to named properties
            if (node.References != null)
            {
                var namedRefs = new Dictionary<string, List<string>>();

                var genericRefs = new JArray();

                foreach (var r in node.References)
                {
                    bool isForward = r.IsForward ?? true;
                    var propName = GetNamedRefProperty(r.ReferenceTypeId, isForward);

                    if (propName != null)
                    {
                        if (!namedRefs.ContainsKey(propName))
                            namedRefs[propName] = new List<string>();
                        namedRefs[propName].Add(ToCurie(r.TargetId));
                    }
                    else
                    {
                        var refObj = new JObject();
                        refObj["referenceType"] = ToCurie(r.ReferenceTypeId);
                        if (!isForward) refObj["isForward"] = false;
                        refObj["target"] = ToCurie(r.TargetId);
                        genericRefs.Add(refObj);
                    }
                }

                foreach (var kvp in namedRefs)
                {
                    if (kvp.Value.Count == 1)
                        obj[kvp.Key] = kvp.Value[0];
                    else
                        obj[kvp.Key] = new JArray(kvp.Value);
                }

                if (genericRefs.Count > 0)
                    obj["references"] = genericRefs;
            }

            // DataTypeDefinition
            if (node is Json.UADataType dt && dt.Definition != null)
            {
                var defObj = new JObject();

                if (dt.Definition.IsUnion != null)
                    defObj["isUnion"] = dt.Definition.IsUnion.Value;

                if (dt.Definition.Fields != null && dt.Definition.Fields.Count > 0)
                {
                    var fieldsArray = new JArray();
                    foreach (var field in dt.Definition.Fields)
                    {
                        var fieldObj = new JObject();
                        fieldObj["fieldName"] = field.Name;

                        if (field.Value != null)
                            fieldObj["fieldValue"] = field.Value;

                        if (field.DataType != null)
                            fieldObj["fieldDataType"] = ToCurie(field.DataType);

                        if (field.ValueRank != null)
                            fieldObj["valueRank"] = field.ValueRank;

                        if (field.IsOptional == true)
                            fieldObj["isOptional"] = true;

                        if (field.AllowSubTypes == true)
                            fieldObj["allowSubTypes"] = true;

                        fieldsArray.Add(fieldObj);
                    }
                    defObj["fields"] = fieldsArray;
                }

                obj["definition"] = defObj;
            }

            return obj;
        }

        private static string NodeClassToJsonLdType(Json.UANode node)
        {
            return node switch
            {
                Json.UADataType => "opcua:UADataType",
                Json.UAObjectType => "opcua:UAObjectType",
                Json.UAVariableType => "opcua:UAVariableType",
                Json.UAReferenceType => "opcua:UAReferenceType",
                Json.UAObject => "opcua:UAObject",
                Json.UAVariable => "opcua:UAVariable",
                Json.UAMethod => "opcua:UAMethod",
                Json.UAView => "opcua:UAView",
                _ => "opcua:UANode"
            };
        }

        private static string? GetNamedRefProperty(string? refTypeId, bool isForward)
        {
            return (refTypeId, isForward) switch
            {
                ("i=45", true) => "HasSubtype",
                ("i=45", false) => "SubtypeOf",
                ("i=46", true) => "HasProperty",
                ("i=46", false) => "PropertyOf",
                ("i=47", true) => "HasComponent",
                ("i=47", false) => "ComponentOf",
                ("i=35", true) => "Organizes",
                ("i=35", false) => "OrganizedBy",
                ("i=38", true) => "HasEncoding",
                ("i=38", false) => "EncodingOf",
                ("i=39", true) => "HasDescription",
                ("i=14156", true) => "HasOrderedComponent",
                ("i=14156", false) => "OrderedComponentOf",
                ("i=49", true) => "HasOrderedComponent",
                ("i=49", false) => "OrderedComponentOf",
                _ => null
            };
        }

        private static (string refTypeId, bool isForward)? GetRefFromProperty(string propertyName)
        {
            return propertyName switch
            {
                "HasSubtype" => ("i=45", true),
                "SubtypeOf" => ("i=45", false),
                "HasProperty" => ("i=46", true),
                "PropertyOf" => ("i=46", false),
                "HasComponent" => ("i=47", true),
                "ComponentOf" => ("i=47", false),
                "Organizes" => ("i=35", true),
                "OrganizedBy" => ("i=35", false),
                "HasEncoding" => ("i=38", true),
                "EncodingOf" => ("i=38", false),
                "HasDescription" => ("i=39", true),
                "HasOrderedComponent" => ("i=49", true),
                "OrderedComponentOf" => ("i=49", false),
                _ => null
            };
        }

        private static Json.UANode CreateNodeFromJsonLdType(string type)
        {
            var cleanType = type.Replace("opcua:", "");
            return cleanType switch
            {
                "UADataType" => new Json.UADataType { NodeClass = Json.NodeClass.UADataType },
                "UAObjectType" => new Json.UAObjectType { NodeClass = Json.NodeClass.UAObjectType },
                "UAVariableType" => new Json.UAVariableType { NodeClass = Json.NodeClass.UAVariableType },
                "UAReferenceType" => new Json.UAReferenceType { NodeClass = Json.NodeClass.UAReferenceType },
                "UAVariable" => new Json.UAVariable { NodeClass = Json.NodeClass.UAVariable },
                "UAMethod" => new Json.UAMethod { NodeClass = Json.NodeClass.UAMethod },
                "UAView" => new Json.UAView { NodeClass = Json.NodeClass.UAView },
                _ => new Json.UAObject { NodeClass = Json.NodeClass.UAObject }
            };
        }

        private Json.UANode? JsonLdEntryToNode(JObject obj, Dictionary<string, string> prefixToUri)
        {
            var type = obj["@type"]?.ToString();
            if (type == null) return null;

            var node = CreateNodeFromJsonLdType(type);

            var id = obj["@id"]?.ToString();
            node.NodeId = id != null ? FromCurie(id, prefixToUri) : null;
            node.BrowseName = obj["browseName"]?.ToString();
            node.SymbolicName = obj["symbolicName"]?.ToString();

            var desc = obj["description"]?.ToString();
            if (desc != null)
                node.Description = new Json.LocalizedText { T = new List<List<string>> { new List<string> { "", desc } } };

            var rs = obj["releaseStatus"]?.ToString();
            if (rs != null)
            {
                node.ReleaseStatus = rs switch
                {
                    "Draft" => Json.ReleaseStatus.Draft,
                    "Deprecated" => Json.ReleaseStatus.Deprecated,
                    _ => null
                };
            }

            var typeDef = obj["typeDefinition"]?.ToString();
            if (typeDef != null) node.TypeId = FromCurie(typeDef, prefixToUri);

            var mr = obj["modellingRule"]?.ToString();
            if (mr != null) node.ModellingRuleId = FromCurie(mr, prefixToUri);

            var parentVal = obj["parent"]?.ToString();
            if (parentVal != null) node.ParentId = FromCurie(parentVal, prefixToUri);

            // dataType, valueRank, arrayDimensions
            var dataTypeVal = obj["dataType"]?.ToString();
            var valueRankVal = obj["valueRank"]?.Value<int?>();
            var arrayDimsVal = obj["arrayDimensions"]?.ToString();

            if (node is Json.UAVariable v)
            {
                if (dataTypeVal != null) v.DataType = FromCurie(dataTypeVal, prefixToUri);
                v.ValueRank = valueRankVal;
                v.ArrayDimensions = arrayDimsVal;
            }
            if (node is Json.UAVariableType vt)
            {
                if (dataTypeVal != null) vt.DataType = FromCurie(dataTypeVal, prefixToUri);
                vt.ValueRank = valueRankVal;
                vt.ArrayDimensions = arrayDimsVal;
            }

            var arVal = obj["accessRestrictions"]?.Value<long?>();
            if (arVal != null) node.AccessRestrictions = arVal;

            // rolePermissions
            var rps = obj["rolePermissions"] as JArray;
            if (rps != null)
            {
                node.RolePermissions = new List<Json.RolePermission>();
                foreach (var rp in rps)
                {
                    var roleId = rp["roleId"]?.ToString();
                    var perms = rp["permissions"]?.Value<long?>();
                    node.RolePermissions.Add(new Json.RolePermission(
                        roleId != null ? FromCurie(roleId, prefixToUri) : null,
                        perms ?? 0));
                }
            }

            // Named reference properties → References
            // Iterate JObject properties in their natural order to preserve serialization order
            var refs = new List<Json.Reference>();

            foreach (var prop in obj.Properties())
            {
                var refInfo = GetRefFromProperty(prop.Name);
                if (refInfo == null) continue;

                if (prop.Value.Type == JTokenType.Array)
                {
                    foreach (var item in (JArray)prop.Value)
                    {
                        refs.Add(new Json.Reference
                        {
                            ReferenceTypeId = refInfo.Value.refTypeId,
                            IsForward = refInfo.Value.isForward ? null : false,
                            TargetId = FromCurie(item.ToString(), prefixToUri)
                        });
                    }
                }
                else if (prop.Value.Type == JTokenType.String)
                {
                    refs.Add(new Json.Reference
                    {
                        ReferenceTypeId = refInfo.Value.refTypeId,
                        IsForward = refInfo.Value.isForward ? null : false,
                        TargetId = FromCurie(prop.Value.ToString(), prefixToUri)
                    });
                }
            }

            // Generic references array for unmapped reference types
            var genericRefs = obj["references"] as JArray;
            if (genericRefs != null)
            {
                foreach (var gr in genericRefs)
                {
                    var refTypeId = gr["referenceType"]?.ToString();
                    var isForward = gr["isForward"]?.Value<bool?>() ?? true;
                    var target = gr["target"]?.ToString();
                    if (refTypeId != null && target != null)
                    {
                        refs.Add(new Json.Reference
                        {
                            ReferenceTypeId = FromCurie(refTypeId, prefixToUri),
                            IsForward = isForward ? null : false,
                            TargetId = FromCurie(target, prefixToUri)
                        });
                    }
                }
            }

            node.References = refs.Count > 0 ? refs : null;

            // DataTypeDefinition
            if (node is Json.UADataType dt)
            {
                var def = obj["definition"] as JObject;
                if (def != null)
                {
                    dt.Definition = new Json.DataTypeDefinition();
                    dt.Definition.IsUnion = def["isUnion"]?.Value<bool?>();

                    var fields = def["fields"] as JArray;
                    if (fields != null)
                    {
                        dt.Definition.Fields = new List<Json.DataTypeField>();
                        foreach (var field in fields)
                        {
                            var f = new Json.DataTypeField();
                            f.Name = field["fieldName"]?.ToString();
                            f.Value = field["fieldValue"]?.Value<int?>();
                            var fdt = field["fieldDataType"]?.ToString();
                            if (fdt != null) f.DataType = FromCurie(fdt, prefixToUri);
                            f.ValueRank = field["valueRank"]?.Value<int?>();
                            f.IsOptional = field["isOptional"]?.Value<bool?>();
                            f.AllowSubTypes = field["allowSubTypes"]?.Value<bool?>();
                            dt.Definition.Fields.Add(f);
                        }
                    }
                }
            }

            return node;
        }

        #endregion
    }
}
