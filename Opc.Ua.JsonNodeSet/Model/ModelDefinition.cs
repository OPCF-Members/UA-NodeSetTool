using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class ModelDefinition : ModelReference
{
    [DataMember]
    public long? DefaultAccessRestrictions { get; set; }

    [DataMember]
    public List<RolePermission>? DefaultRolePermissions { get; set; }

    [DataMember]
    public List<ModelReference>? RequiredModels { get; set; }

    [DataMember]
    public bool? IsPartial { get; set; }
}
