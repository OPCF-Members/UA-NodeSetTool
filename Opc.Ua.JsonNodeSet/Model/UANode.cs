using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UANode
{
    [DataMember]
    public string? NodeId { get; set; }

    [DataMember]
    public NodeClass? NodeClass { get; set; }

    [DataMember]
    public string? SymbolicName { get; set; }

    [DataMember]
    public string? BrowseName { get; set; }

    [DataMember]
    public LocalizedText? DisplayName { get; set; }

    [DataMember]
    public LocalizedText? Description { get; set; }

    [DataMember]
    public long? WriteMask { get; set; }

    [DataMember]
    public string? Documentation { get; set; }

    [DataMember]
    public ReleaseStatus? ReleaseStatus { get; set; }

    [DataMember]
    public string? ParentId { get; set; }

    [DataMember]
    public string? TypeId { get; set; }

    [DataMember]
    public string? ModellingRuleId { get; set; }

    [DataMember]
    public bool? DesignToolOnly { get; set; }

    [DataMember]
    public bool? IsAbstract { get; set; }

    [DataMember]
    public List<RolePermission>? RolePermissions { get; set; }

    [DataMember]
    public long? AccessRestrictions { get; set; }

    [DataMember]
    public bool? HasNoPermissions { get; set; }

    [DataMember]
    public ChildList? Children { get; set; }

    [DataMember]
    public List<Reference>? References { get; set; }

    [DataMember]
    public List<string>? ConformationUnits { get; set; }
}
