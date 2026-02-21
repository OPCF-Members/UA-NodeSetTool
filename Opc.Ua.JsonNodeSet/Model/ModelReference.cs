using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class ModelReference
{
    [DataMember]
    public string? ModelUri { get; set; }

    [DataMember]
    public string? XmlSchemaUri { get; set; }

    [DataMember]
    public DateTime? PublicationDate { get; set; }

    [DataMember(Name = "Version")]
    [JsonProperty("Version")]
    public string? VarVersion { get; set; }

    [DataMember]
    public string? ModelVersion { get; set; }
}
