namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;

public interface ILayerProcessor
{
    GenerationResult AddIdentityLayer(GenerationResult input, LayerProcessingOptions? options = null);
    GenerationResult AddInfrastructureLayer(GenerationResult input, LayerProcessingOptions? options = null);
    GenerationResult AddRepositoryLayer(GenerationResult input, LayerProcessingOptions? options = null);
    GenerationResult ApplyAnomalyProfiles(GenerationResult input, LayerProcessingOptions? options = null);
}
