namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Models;

public interface IWorldOwnershipReconciliationService
{
    WorldOwnershipReconciliationResult Reconcile(SyntheticEnterpriseWorld world);
}

public sealed record WorldOwnershipReconciliationResult
{
    public int UpdatedCount { get; init; }
    public int RemovedCount { get; init; }
    public List<string> Warnings { get; init; } = new();
}
