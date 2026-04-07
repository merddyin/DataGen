using SyntheticEnterprise.Core.Contracts;

namespace SyntheticEnterprise.Core.Services;

public sealed class SchemaCompatibilityService : ISchemaCompatibilityService
{
    public SchemaCompatibilityAssessment Assess(string currentSchemaVersion, string importedSchemaVersion)
    {
        if (currentSchemaVersion == importedSchemaVersion)
        {
            return new SchemaCompatibilityAssessment { Level = CompatibilityLevel.Compatible };
        }

        var currentMajor = ParseMajor(currentSchemaVersion);
        var importedMajor = ParseMajor(importedSchemaVersion);

        if (currentMajor == importedMajor)
        {
            return new SchemaCompatibilityAssessment
            {
                Level = CompatibilityLevel.CompatibleWithWarnings,
                Messages = new[]
                {
                    $"Imported schema version '{importedSchemaVersion}' differs from current '{currentSchemaVersion}' but shares the same major version."
                }
            };
        }

        return new SchemaCompatibilityAssessment
        {
            Level = CompatibilityLevel.Incompatible,
            Messages = new[]
            {
                $"Imported schema version '{importedSchemaVersion}' is not compatible with current '{currentSchemaVersion}'."
            }
        };
    }

    private static int ParseMajor(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return -1;
        }

        var first = version.Split('.')[0];
        return int.TryParse(first, out var value) ? value : -1;
    }
}
