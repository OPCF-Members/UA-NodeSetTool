using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class ArgumentDescription
{
    [DataMember]
    public string? Name { get; set; }

    [DataMember]
    public LocalizedText? DisplayName { get; set; }
}
