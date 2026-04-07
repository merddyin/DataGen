using SyntheticEnterprise.Contracts;

namespace SyntheticEnterprise.Services;

public sealed class RegenerationPlanner : IRegenerationPlanner
{
    public LayerRegenerationPolicy CreatePolicy(string layerName, string regenerationMode)
    {
        return new LayerRegenerationPolicy
        {
            LayerName = layerName,
            RegenerationMode = regenerationMode,
            PreserveStableIdentifiersWhenPossible = regenerationMode is "ReplaceLayer" or "Merge",
            AttemptDownstreamReferenceRepair = regenerationMode is "ReplaceLayer" or "Merge",
            WarnOnUnrepairedReferences = true,
            DefaultMergeConflictResolution = "PreserveExisting",
        };
    }
}
