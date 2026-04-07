using SyntheticEnterprise.Core.Contracts;

namespace SyntheticEnterprise.Core.Services;

public interface ISchemaCompatibilityService
{
    SchemaCompatibilityAssessment Assess(string currentSchemaVersion, string importedSchemaVersion);
}
