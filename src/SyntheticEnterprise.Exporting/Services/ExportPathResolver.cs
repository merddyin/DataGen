using System;
using System.IO;

namespace SyntheticEnterprise.Exporting.Services;

public sealed class ExportPathResolver : IExportPathResolver
{
    public string ResolveRoot(string outputPath, string? artifactPrefix)
    {
        var prefix = string.IsNullOrWhiteSpace(artifactPrefix)
            ? $"synthetic_enterprise_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
            : artifactPrefix.Trim();

        return Path.Combine(outputPath, prefix);
    }
}
