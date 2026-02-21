using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UADataType : UANode
{
    [DataMember]
    public DataTypeDefinition? Definition { get; set; }

    [DataMember]
    public DataTypePurpose? Purpose { get; set; }
}
