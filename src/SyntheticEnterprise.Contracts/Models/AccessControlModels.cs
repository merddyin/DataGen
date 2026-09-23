namespace SyntheticEnterprise.Contracts.Models;

/// <summary>
/// Inheritance scope values an access control entry on a directory object can carry. These describe
/// how far down the container hierarchy the entry itself reaches; they say nothing about Group
/// Policy link inheritance, which is a separate aspect of a container carried on
/// <see cref="EnvironmentContainer.BlocksPolicyInheritance"/>.
/// </summary>
public static class AccessControlInheritanceScope
{
    /// <summary>The entry applies to the object it is set on and to no descendant.</summary>
    public const string ThisObjectOnly = "ThisObjectOnly";

    /// <summary>
    /// The entry applies to the object it is set on and to every descendant of that object, until
    /// propagation is halted by a descendant whose discretionary access control list is protected
    /// from inheritance.
    /// </summary>
    public const string ThisObjectAndAllDescendants = "ThisObjectAndAllDescendants";

    /// <summary>
    /// The collection source read the entry from the object's security descriptor but recorded no
    /// propagation flag for it, and the right the entry carries does not fix one either. It states
    /// that how far this entry reaches is not known - not that it reaches nothing. Treating it as
    /// <see cref="ThisObjectOnly"/> or as <see cref="ThisObjectAndAllDescendants"/> would be a
    /// guess; the value exists so the record says so itself rather than leaving a reader to infer
    /// it from an empty field.
    /// </summary>
    public const string NotRecorded = "NotRecorded";
}

/// <summary>
/// One access control entry, as the system it was read from holds it.
///
/// An entry held on the security descriptor of an organizational unit has exactly one shape:
/// <see cref="TargetType"/> is <c>DirectoryOrganizationalUnit</c> and <see cref="TargetId"/> is the
/// identifier of the <see cref="DirectoryOrganizationalUnit"/>. No other target type ever denotes an
/// organizational unit, so a reader selects on the target type alone and needs to know nothing about
/// which part of the world produced the row. An <see cref="EnvironmentContainer"/> that mirrors an
/// organizational unit is not a second place to look: entries are held against the unit itself, and
/// <c>Container</c> entries name containers that are not organizational units - a directory domain,
/// a default directory container, an administrative unit, a subscription, a resource group.
///
/// Every entry whose target type is <c>DirectoryOrganizationalUnit</c> carries a non-null
/// <see cref="InheritanceScope"/>. For other target types the field may be null, because the system
/// the entry came from has no container hierarchy for it to describe.
/// </summary>
public record AccessControlEvidenceRecord
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string PrincipalObjectId { get; init; } = "";
    public string PrincipalType { get; init; } = "";

    /// <summary>
    /// What the entry is held on. <c>DirectoryOrganizationalUnit</c> is the only value that denotes
    /// an organizational unit; see the remarks on this record.
    /// </summary>
    public string TargetType { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string RightName { get; init; } = "";
    public string AccessType { get; init; } = "Allow";
    public bool IsInherited { get; init; }
    public bool IsDefaultEntry { get; init; }
    public string SourceSystem { get; init; } = "";
    public string? InheritanceSourceId { get; init; }

    /// <summary>
    /// How far the entry reaches from the object it is set on: one of the values on
    /// <see cref="AccessControlInheritanceScope"/>.
    ///
    /// Never null when <see cref="TargetType"/> is <c>DirectoryOrganizationalUnit</c>. Where the
    /// source recorded no propagation flag and the right itself does not fix one, the value is
    /// <see cref="AccessControlInheritanceScope.NotRecorded"/> rather than null, so a reader of a
    /// single row can tell "not known" from "not populated".
    ///
    /// Null for target types with no container hierarchy for a scope to describe, such as an entry
    /// read from a policy object, an application or a repository.
    /// </summary>
    public string? InheritanceScope { get; init; }

    public string? Notes { get; init; }
}
