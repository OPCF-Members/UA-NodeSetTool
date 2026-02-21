using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UAVariable : UANode
{
    [DataMember]
    public Variant? Value { get; set; }

    [DataMember]
    public string? DataType { get; set; }

    [DataMember]
    public int? ValueRank { get; set; }

    [DataMember]
    public string? ArrayDimensions { get; set; }

    [DataMember]
    public long? AccessLevel { get; set; }

    [DataMember]
    public decimal? MinimumSamplingInterval { get; set; }

    [DataMember]
    public bool? Historizing { get; set; }
}
