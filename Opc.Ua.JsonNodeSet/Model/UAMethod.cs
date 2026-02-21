using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UAMethod : UANode
{
    [DataMember]
    public bool? Executable { get; set; }

    [DataMember]
    public string? MethodDeclarationId { get; set; }

    [DataMember]
    public List<ArgumentDescription>? Arguments { get; set; }
}
