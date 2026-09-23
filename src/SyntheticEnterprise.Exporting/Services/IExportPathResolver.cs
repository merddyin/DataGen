using System;

namespace SyntheticEnterprise.Exporting.Services;

public interface IExportPathResolver
{
    /// <summary>
    /// Resolves the directory an export writes into.
    /// </summary>
    /// <param name="outputPath">The caller supplied output path.</param>
    /// <param name="artifactPrefix">An explicit directory name, or <see langword="null"/> to derive one.</param>
    /// <param name="exportedAtUtc">
    /// The export timestamp recorded in the manifest. A derived directory name is built from this value, never from
    /// the wall clock, so that repeating a run with identical inputs resolves to an identical path.
    /// </param>
    string ResolveRoot(string outputPath, string? artifactPrefix, DateTimeOffset exportedAtUtc);
}
