namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Models;

public interface IWorldInvariantValidator
{
    WorldInvariantValidationResult Validate(SyntheticEnterpriseWorld world);
}

public sealed record WorldInvariantValidationResult
{
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
