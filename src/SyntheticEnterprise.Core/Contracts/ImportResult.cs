namespace SyntheticEnterprise.Core.Contracts;

public sealed class ImportResult<T>
{
    public required T Payload { get; init; }
    public required SnapshotEnvelope<T> Envelope { get; init; }
    public required SchemaCompatibilityAssessment Compatibility { get; init; }
}
