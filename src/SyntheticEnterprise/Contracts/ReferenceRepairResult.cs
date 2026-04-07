using System.Collections.Generic;

namespace SyntheticEnterprise.Contracts;

public sealed class ReferenceRepairResult
{
    public List<ReferenceRepairRecord> Records { get; } = new();
    public List<string> Warnings { get; } = new();
}
