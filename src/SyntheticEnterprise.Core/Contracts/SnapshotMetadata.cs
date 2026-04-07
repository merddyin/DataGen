using System;

namespace SyntheticEnterprise.Core.Contracts;

public sealed class SnapshotMetadata
{
    public Guid SnapshotId { get; set; } = Guid.NewGuid();
    public string? SourceScenarioPath { get; set; }
    public string? SourceScenarioName { get; set; }
    public CatalogContentFingerprint? CatalogFingerprint { get; set; }
    public string[] Warnings { get; set; } = Array.Empty<string>();
}
