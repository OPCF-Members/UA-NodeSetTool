using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class DataTypeDefinition
{
    [DataMember]
    public string? Name { get; set; }

    [DataMember]
    public string? SymbolicName { get; set; }

    [DataMember]
    public bool? IsUnion { get; set; }

    [DataMember]
    public bool? IsOptionSet { get; set; }

    [DataMember]
    public List<DataTypeField>? Fields { get; set; }
}
