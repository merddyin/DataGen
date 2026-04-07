using SyntheticEnterprise.Contracts;

namespace SyntheticEnterprise.Services;

/// <summary>
/// Merge scaffold for Milestone 3. This intentionally does not perform deep entity merging yet.
/// It establishes the result shape and conservative behavior expected by the cmdlets and tests.
/// </summary>
public sealed class LayerMergeService : ILayerMergeService
{
    public LayerMutationResult MergeLayer(object currentWorld, object candidateWorld, LayerRegenerationPolicy policy)
    {
        var result = new LayerMutationResult
        {
            LayerName = policy.LayerName,
            RegenerationMode = policy.RegenerationMode,
            Outcome = RegenerationExecutionOutcome.Merged,
        };

        result.Warnings.Add("Merge executed with conservative preserve-existing semantics. Deep conflict-aware merge is not yet implemented.");
        return result;
    }
}
