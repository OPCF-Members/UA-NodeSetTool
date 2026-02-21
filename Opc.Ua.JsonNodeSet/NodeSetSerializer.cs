using Xml = Opc.Ua.Export;
using Json = Opc.Ua.JsonNodeSet.Model;
using Opc.Ua;
using System.Xml;
using Newtonsoft.Json;
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
        private Dictionary<string,string>? m_aliases;
        private Dictionary<string, Json.ModelDefinition>? m_models;
        private Dictionary<string, Json.UANode>? m_nodes;
        private List<Json.UANode>? m_sequence;
        private List<CompareError> m_errors = new();

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
            if (original.NodeClass != target.NodeClass)  { m_errors.Add(new CompareError(original, nameof(Json.UANode.NodeClass), original.NodeClass, target.NodeClass)); return false; }
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

        public void SaveXml(string filePath)
        {
            var xml = BuildXml();
            using var ostrm = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite);
            xml.Write(ostrm);
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
                    m_nodes![child.NodeId!] = child;
                    m_sequence!.Add(child);
                    IndexChildren(child);
                }
            }

            if (parent.Children.Variables != null)
            {
                foreach (var child in parent.Children.Variables)
                {
                    child.ParentId = parent.NodeId;
                    m_nodes![child.NodeId!] = child;
                    m_sequence!.Add(child);
                    IndexChildren(child);
                }
            }

            if (parent.Children.Methods != null)
            {
                foreach (var child in parent.Children.Methods)
                {
                    child.ParentId = parent.NodeId;
                    m_nodes![child.NodeId!] = child;
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
                    case Json.UAObject on: { nodes.Add(ToXmlNode(on));  break; }
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
                Current = currentFileCount+1,
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
    }
}
