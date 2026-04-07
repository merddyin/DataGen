using System;

namespace SyntheticEnterprise.Exporting.Contracts;

public sealed class ExportRequest
{
    public ExportSerializationFormat Format { get; init; } = ExportSerializationFormat.Csv;
    public ExportProfileKind Profile { get; init; } = ExportProfileKind.Normalized;
    public string OutputPath { get; init; } = string.Empty;
    public string? ArtifactPrefix { get; init; }
    public bool IncludeManifest { get; init; } = true;
    public bool IncludeSummary { get; init; } = true;
    public bool Overwrite { get; init; }
    public DateTimeOffset ExportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
