using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UAObject : UANode
{
    [DataMember]
    public int? EventNotifier { get; set; }
}
