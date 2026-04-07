using SyntheticEnterprise.Module.Contracts;

namespace SyntheticEnterprise.Module.Services;

public interface IWorldValidationService
{
    IReadOnlyList<ValidationIssue> Validate(object generationResult);
}
