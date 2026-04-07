using System;
using System.Collections.Generic;

namespace SyntheticEnterprise.Exporting.Contracts;

public sealed class ExportManifestV2
{
    public required string ExportId { get; init; }
    public required string SchemaVersion { get; init; }
    public required ExportSerializationFormat Format { get; init; }
    public required ExportProfileKind Profile { get; init; }
    public required DateTimeOffset ExportedAtUtc { get; init; }
    public required string OutputPath { get; init; }
    public IReadOnlyList<ExportArtifactDescriptor> Artifacts { get; init; } = [];
}
