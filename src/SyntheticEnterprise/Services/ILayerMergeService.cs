using SyntheticEnterprise.Contracts;

namespace SyntheticEnterprise.Services;

public interface ILayerMergeService
{
    LayerMutationResult MergeLayer(object currentWorld, object candidateWorld, LayerRegenerationPolicy policy);
}
