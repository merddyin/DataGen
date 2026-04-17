namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Models;

public interface IWorldLayerRemapService
{
    WorldLayerRemapResult RemapAfterIdentityReplacement(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld);
    WorldLayerRemapResult MergeAfterIdentityRegeneration(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld);

    WorldLayerRemapResult RemapAfterInfrastructureReplacement(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld);
    WorldLayerRemapResult MergeAfterInfrastructureRegeneration(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld);

    WorldLayerRemapResult RemapAfterRepositoryReplacement(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld);
    WorldLayerRemapResult MergeAfterRepositoryRegeneration(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld);
}

public sealed record WorldLayerRemapResult
{
    public int UpdatedCount { get; init; }

    public List<string> Warnings { get; init; } = new();
}
