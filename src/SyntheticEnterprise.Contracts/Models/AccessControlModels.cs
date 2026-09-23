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
}

public record AccessControlEvidenceRecord
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string PrincipalObjectId { get; init; } = "";
    public string PrincipalType { get; init; } = "";
    public string TargetType { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string RightName { get; init; } = "";
    public string AccessType { get; init; } = "Allow";
    public bool IsInherited { get; init; }
    public bool IsDefaultEntry { get; init; }
    public string SourceSystem { get; init; } = "";
    public string? InheritanceSourceId { get; init; }

    /// <summary>
    /// How far the entry reaches from the object it is set on: see
    /// <see cref="AccessControlInheritanceScope"/>. Null where the collection source does not
    /// express a scope for the target, as is the case for entries read from a policy object rather
    /// than from a directory object's security descriptor.
    /// </summary>
    public string? InheritanceScope { get; init; }

    public string? Notes { get; init; }
}
