namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Models;

public interface IWorldQualityAuditService
{
    WorldQualityAuditResult Audit(SyntheticEnterpriseWorld world);
}

public sealed record WorldQualityAuditResult
{
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
