namespace SyntheticEnterprise.Contracts.Abstractions;

public record ExportArtifact
{
    public string LogicalName { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Format { get; init; } = "";
    public int RecordCount { get; init; }
}

public record ExportManifest
{
    public string ExportedAtUtc { get; init; } = "";
    public string Format { get; init; } = "";
    public string OutputPath { get; init; } = "";
    public List<ExportArtifact> Artifacts { get; init; } = new();
}

public record ExportResult
{
    public string OutputPath { get; init; } = "";
    public required ExportManifest Manifest { get; init; }
}
