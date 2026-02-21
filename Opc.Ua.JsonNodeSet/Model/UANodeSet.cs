using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UANodeSet
{
    [DataMember]
    public List<ModelDefinition>? Models { get; set; }

    [DataMember]
    public FileSetInfo? FileSet { get; set; }

    [DataMember]
    public UANodeSetNodes? Nodes { get; set; }
}
