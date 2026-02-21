#pragma warning disable 1591
using System.Xml;
using System.Xml.Serialization;

namespace Opc.Ua.Export
{
    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    [XmlRoot("UANodeSet", Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UANodeSet
    {
        [XmlArray("NamespaceUris")]
        [XmlArrayItem("Uri")]
        public string[]? NamespaceUris { get; set; }

        [XmlArray("ServerUris")]
        [XmlArrayItem("Uri")]
        public string[]? ServerUris { get; set; }

        [XmlArray("Models")]
        [XmlArrayItem("Model")]
        public ModelTableEntry[]? Models { get; set; }

        [XmlArray("Aliases")]
        [XmlArrayItem("Alias")]
        public NodeIdAlias[]? Aliases { get; set; }

        [XmlElement("UAObject", typeof(UAObject))]
        [XmlElement("UAVariable", typeof(UAVariable))]
        [XmlElement("UAMethod", typeof(UAMethod))]
        [XmlElement("UAView", typeof(UAView))]
        [XmlElement("UAObjectType", typeof(UAObjectType))]
        [XmlElement("UAVariableType", typeof(UAVariableType))]
        [XmlElement("UADataType", typeof(UADataType))]
        [XmlElement("UAReferenceType", typeof(UAReferenceType))]
        public UANode[]? Items { get; set; }

        [XmlAttribute]
        public DateTime LastModified { get; set; }

        [XmlIgnore]
        public bool LastModifiedSpecified { get; set; }

        public static UANodeSet? Read(Stream stream)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            var serializer = new XmlSerializer(typeof(UANodeSet));
            using var reader = XmlReader.Create(stream, settings);
            return (UANodeSet?)serializer.Deserialize(reader);
        }

        public void Write(Stream stream)
        {
            var settings = new XmlWriterSettings
            {
                IndentChars = "  ",
                Indent = true,
                Encoding = System.Text.Encoding.UTF8,
                CloseOutput = true
            };

            var serializer = new XmlSerializer(typeof(UANodeSet));
            using var writer = XmlWriter.Create(stream, settings);
            serializer.Serialize(writer, this);
        }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class ModelTableEntry
    {
        [XmlElement("RolePermissions")]
        public RolePermission[]? RolePermissions { get; set; }

        [XmlElement("RequiredModel")]
        public ModelTableEntry[]? RequiredModel { get; set; }

        [XmlAttribute]
        public string? ModelUri { get; set; }

        [XmlAttribute]
        public string? XmlSchemaUri { get; set; }

        [XmlAttribute]
        public string? Version { get; set; }

        [XmlAttribute]
        public DateTime PublicationDate { get; set; }

        [XmlIgnore]
        public bool PublicationDateSpecified { get; set; }

        [XmlAttribute]
        public string? ModelVersion { get; set; }

        [XmlAttribute(DataType = "unsignedShort")]
        public ushort AccessRestrictions { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class NodeIdAlias
    {
        [XmlAttribute]
        public string? Alias { get; set; }

        [XmlText]
        public string? Value { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class LocalizedText
    {
        [XmlAttribute]
        public string? Locale { get; set; }

        [XmlText]
        public string? Value { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class Reference
    {
        [XmlAttribute]
        public string? ReferenceType { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(true)]
        public bool IsForward { get; set; } = true;

        [XmlText]
        public string? Value { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class RolePermission
    {
        [XmlAttribute(DataType = "unsignedInt")]
        public uint Permissions { get; set; }

        [XmlText]
        public string? Value { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public enum ReleaseStatus
    {
        Released,
        Draft,
        Deprecated
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public enum DataTypePurpose
    {
        Normal,
        ServicesOnly,
        CodeGenerator
    }

    [XmlInclude(typeof(UAObject))]
    [XmlInclude(typeof(UAVariable))]
    [XmlInclude(typeof(UAMethod))]
    [XmlInclude(typeof(UAView))]
    [XmlInclude(typeof(UAObjectType))]
    [XmlInclude(typeof(UAVariableType))]
    [XmlInclude(typeof(UADataType))]
    [XmlInclude(typeof(UAReferenceType))]
    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UANode
    {
        [XmlElement("DisplayName")]
        public LocalizedText[]? DisplayName { get; set; }

        [XmlElement("Description")]
        public LocalizedText[]? Description { get; set; }

        [XmlElement("Category")]
        public string[]? Category { get; set; }

        public string? Documentation { get; set; }

        [XmlArray("References")]
        [XmlArrayItem("Reference")]
        public Reference[]? References { get; set; }

        [XmlArray("RolePermissions")]
        [XmlArrayItem("RolePermission")]
        public RolePermission[]? RolePermissions { get; set; }

        [XmlAttribute]
        public string? NodeId { get; set; }

        [XmlAttribute]
        public string? BrowseName { get; set; }

        [XmlAttribute(DataType = "unsignedInt")]
        public uint WriteMask { get; set; }

        [XmlAttribute(DataType = "unsignedInt")]
        public uint UserWriteMask { get; set; }

        [XmlAttribute(DataType = "unsignedShort")]
        public ushort AccessRestrictions { get; set; }

        [XmlIgnore]
        public bool AccessRestrictionsSpecified { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool HasNoPermissions { get; set; }

        [XmlAttribute]
        public string? SymbolicName { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(ReleaseStatus.Released)]
        public ReleaseStatus ReleaseStatus { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UAInstance : UANode
    {
        [XmlAttribute]
        public string? ParentNodeId { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool DesignToolOnly { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UAType : UANode
    {
        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool IsAbstract { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UAObject : UAInstance
    {
        [XmlAttribute(DataType = "unsignedByte")]
        public byte EventNotifier { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UAVariable : UAInstance
    {
        [XmlAnyElement("Value")]
        public XmlElement? Value { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue("i=24")]
        public string DataType { get; set; } = "i=24";

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(-1)]
        public int ValueRank { get; set; } = -1;

        [XmlAttribute]
        public string? ArrayDimensions { get; set; }

        [XmlAttribute(DataType = "unsignedInt")]
        [System.ComponentModel.DefaultValue(typeof(uint), "1")]
        public uint AccessLevel { get; set; } = 1;

        [XmlAttribute(DataType = "unsignedInt")]
        [System.ComponentModel.DefaultValue(typeof(uint), "1")]
        public uint UserAccessLevel { get; set; } = 1;

        [XmlAttribute]
        public double MinimumSamplingInterval { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool Historizing { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UAMethod : UAInstance
    {
        [XmlAttribute]
        [System.ComponentModel.DefaultValue(true)]
        public bool Executable { get; set; } = true;

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(true)]
        public bool UserExecutable { get; set; } = true;

        [XmlAttribute]
        public string? MethodDeclarationId { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UAView : UAInstance
    {
        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool ContainsNoLoops { get; set; }

        [XmlAttribute(DataType = "unsignedByte")]
        public byte EventNotifier { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UAObjectType : UAType
    {
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UAVariableType : UAType
    {
        [XmlAnyElement("Value")]
        public XmlElement? Value { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue("i=24")]
        public string DataType { get; set; } = "i=24";

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(-1)]
        public int ValueRank { get; set; } = -1;

        [XmlAttribute]
        public string? ArrayDimensions { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UADataType : UAType
    {
        public DataTypeDefinition? Definition { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(DataTypePurpose.Normal)]
        public DataTypePurpose Purpose { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class UAReferenceType : UAType
    {
        [XmlElement("InverseName")]
        public LocalizedText[]? InverseName { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool Symmetric { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class DataTypeDefinition
    {
        [XmlElement("Field")]
        public DataTypeField[]? Field { get; set; }

        [XmlAttribute]
        public string? Name { get; set; }

        [XmlAttribute]
        public string? SymbolicName { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool IsUnion { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool IsOptionSet { get; set; }
    }

    [XmlType(Namespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd")]
    public class DataTypeField
    {
        [XmlElement("DisplayName")]
        public LocalizedText[]? DisplayName { get; set; }

        [XmlElement("Description")]
        public LocalizedText[]? Description { get; set; }

        [XmlAttribute]
        public string? Name { get; set; }

        [XmlAttribute]
        public string? SymbolicName { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue("i=24")]
        public string DataType { get; set; } = "i=24";

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(-1)]
        public int ValueRank { get; set; } = -1;

        [XmlAttribute]
        public string? ArrayDimensions { get; set; }

        [XmlAttribute(DataType = "unsignedInt")]
        public uint MaxStringLength { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(-1)]
        public int Value { get; set; } = -1;

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool IsOptional { get; set; }

        [XmlAttribute]
        [System.ComponentModel.DefaultValue(false)]
        public bool AllowSubTypes { get; set; }
    }
}
