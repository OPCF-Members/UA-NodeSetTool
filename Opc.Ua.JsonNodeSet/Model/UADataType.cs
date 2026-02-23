using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UADataType : UANode
{
    [DataMember]
    public DataTypeDefinition? Definition { get; set; }

    [DataMember]
    public DataTypePurpose? Purpose { get; set; }

    /// <summary>
    /// Pre-calculated DataType form based on inheritance chain.
    /// Not serialized — computed after loading into AddressSpace.
    /// Values: "Structure", "Union", "Enumeration", "OptionSet", or null.
    /// </summary>
    public string? DataTypeForm { get; set; }
}
