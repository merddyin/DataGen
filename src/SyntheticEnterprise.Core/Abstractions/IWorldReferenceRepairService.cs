namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Models;

public interface IWorldReferenceRepairService
{
    WorldReferenceRepairResult Repair(SyntheticEnterpriseWorld world);
}

public sealed record WorldReferenceRepairResult
{
    public int RemovedCount { get; init; }
    public int UpdatedCount { get; init; }
    public List<string> Warnings { get; init; } = new();
}
