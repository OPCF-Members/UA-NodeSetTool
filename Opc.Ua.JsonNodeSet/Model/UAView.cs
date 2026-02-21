using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UAView : UANode
{
    [DataMember]
    public int? EventNotifier { get; set; }

    [DataMember]
    public bool? ContainsNoLoops { get; set; }
}
