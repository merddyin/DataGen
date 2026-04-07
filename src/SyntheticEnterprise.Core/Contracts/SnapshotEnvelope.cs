using System;

namespace SyntheticEnterprise.Core.Contracts;

public sealed class SnapshotEnvelope<T>
{
    public string FormatName { get; set; } = SnapshotConstants.FormatName;
    public string SchemaVersion { get; set; } = SnapshotConstants.CurrentSchemaVersion;
    public string GeneratorVersion { get; set; } = SnapshotConstants.CurrentGeneratorVersion;
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
    public bool IsCompressed { get; set; }
    public SnapshotMetadata Metadata { get; set; } = new();
    public T Payload { get; set; } = default!;
}
