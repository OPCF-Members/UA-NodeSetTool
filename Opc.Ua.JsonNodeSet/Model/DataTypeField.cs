using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class DataTypeField
{
    [DataMember]
    public string? Name { get; set; }

    [DataMember]
    public string? SymbolicName { get; set; }

    [DataMember]
    public int? Value { get; set; }

    [DataMember]
    public string? DataType { get; set; }

    [DataMember]
    public int? ValueRank { get; set; }

    [DataMember]
    public string? ArrayDimensions { get; set; }

    [DataMember]
    public int? MaxStringLength { get; set; }

    [DataMember]
    public bool? IsOptional { get; set; }

    [DataMember]
    public bool? AllowSubTypes { get; set; }

    [DataMember]
    public LocalizedText? DisplayName { get; set; }

    [DataMember]
    public LocalizedText? Description { get; set; }
}
