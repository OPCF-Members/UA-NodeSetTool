using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class FileSetInfo
{
    [DataMember]
    public int? Current { get; set; }

    [DataMember]
    public int? Last { get; set; }
}
