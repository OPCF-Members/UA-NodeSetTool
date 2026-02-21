using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UANodeSetNodes
{
    [DataMember]
    public List<UAReferenceType>? N1ReferenceTypes { get; set; }

    [DataMember]
    public List<UADataType>? N2DataTypes { get; set; }

    [DataMember]
    public List<UAVariableType>? N3VariableTypes { get; set; }

    [DataMember]
    public List<UAObjectType>? N4ObjectTypes { get; set; }

    [DataMember]
    public List<UAVariable>? N5Variables { get; set; }

    [DataMember]
    public List<UAMethod>? N6Methods { get; set; }

    [DataMember]
    public List<UAObject>? N7Objects { get; set; }

    [DataMember]
    public List<UAView>? N8Views { get; set; }
}
