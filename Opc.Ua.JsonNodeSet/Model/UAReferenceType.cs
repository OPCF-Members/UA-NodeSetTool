using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UAReferenceType : UANode
{
    [DataMember]
    public bool? Symmetric { get; set; }

    [DataMember]
    public LocalizedText? InverseName { get; set; }
}
