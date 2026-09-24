using System;
using System.Globalization;
using System.IO;

namespace SyntheticEnterprise.Exporting.Services;

public sealed class ExportPathResolver : IExportPathResolver
{
    public string ResolveRoot(string outputPath, string? artifactPrefix, DateTimeOffset exportedAtUtc)
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

        // The derived directory name comes from the supplied export timestamp rather than the wall clock, so that
        // two runs with identical scenario, seed, generation time and export timestamp are path-identical and can
        // be compared or recorded by path. The invariant culture keeps the name Gregorian under any current culture.
        var prefix = string.IsNullOrWhiteSpace(artifactPrefix)
            ? "synthetic_enterprise_export_" + exportedAtUtc.UtcDateTime.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
            : artifactPrefix.Trim();

        return Path.Combine(normalizedOutputPath, prefix);
    }
}
