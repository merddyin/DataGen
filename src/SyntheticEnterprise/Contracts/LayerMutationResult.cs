using System.Collections.Generic;

namespace SyntheticEnterprise.Contracts;

public sealed class LayerMutationResult
{
    public required string LayerName { get; init; }
    public required string RegenerationMode { get; init; }
    public required RegenerationExecutionOutcome Outcome { get; init; }
    public EntityRemappingSet Remapping { get; init; } = new();
    public ReferenceRepairResult ReferenceRepair { get; init; } = new();
    public List<MergeResolutionRecord> MergeResolutions { get; } = new();
    public List<string> Warnings { get; } = new();
    public int ReplacedEntityCount { get; set; }
    public int MergedEntityCount { get; set; }
    public int RemovedEntityCount { get; set; }
}
