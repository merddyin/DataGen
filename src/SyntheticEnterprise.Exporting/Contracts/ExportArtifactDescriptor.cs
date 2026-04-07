using System.Collections.Generic;

namespace SyntheticEnterprise.Exporting.Contracts;

public sealed class ExportArtifactDescriptor
{
    public required string LogicalName { get; init; }
    public required string RelativePath { get; init; }
    public required ExportArtifactKind ArtifactKind { get; init; }
    public required string MediaType { get; init; }
    public required long RowCount { get; init; }
    public required string Sha256 { get; init; }
    public required long SizeBytes { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = [];
}
