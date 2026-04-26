using System;
using System.IO;

namespace SyntheticEnterprise.Exporting.Services;

public sealed class ExportPathResolver : IExportPathResolver
{
    public string ResolveRoot(string outputPath, string? artifactPrefix)
    {
        var normalizedOutputPath = Path.GetFullPath(outputPath);

        if (string.IsNullOrWhiteSpace(artifactPrefix))
        {
            var leafName = Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedOutputPath));
            if (string.Equals(leafName, "normalized", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedOutputPath;
            }
        }

        var prefix = string.IsNullOrWhiteSpace(artifactPrefix)
            ? $"synthetic_enterprise_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
            : artifactPrefix.Trim();

        return Path.Combine(normalizedOutputPath, prefix);
    }
}
