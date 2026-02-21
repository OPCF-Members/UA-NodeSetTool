using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UAVariableType : UANode
{
    [DataMember]
    public Variant? Value { get; set; }

    [DataMember]
    public string? DataType { get; set; }

    [DataMember]
    public int? ValueRank { get; set; }

    [DataMember]
    public string? ArrayDimensions { get; set; }
}
