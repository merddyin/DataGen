using System;

namespace SyntheticEnterprise.Core.Contracts;

public sealed class SchemaCompatibilityAssessment
{
    public CompatibilityLevel Level { get; set; }
    public string[] Messages { get; set; } = Array.Empty<string>();
}

public enum CompatibilityLevel
{
    Compatible = 0,
    CompatibleWithWarnings = 1,
    Incompatible = 2
}
