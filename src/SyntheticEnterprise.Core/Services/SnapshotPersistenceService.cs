using System;
using SyntheticEnterprise.Core.Contracts;
using SyntheticEnterprise.Core.Serialization;

namespace SyntheticEnterprise.Core.Services;

public sealed class SnapshotPersistenceService : ISnapshotPersistenceService
{
    private readonly ISnapshotSerializer _serializer;
    private readonly ISchemaCompatibilityService _compatibilityService;

    public SnapshotPersistenceService(
        ISnapshotSerializer serializer,
        ISchemaCompatibilityService compatibilityService)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _compatibilityService = compatibilityService ?? throw new ArgumentNullException(nameof(compatibilityService));
    }

    public SnapshotEnvelope<T> CreateEnvelope<T>(
        T payload,
        CatalogContentFingerprint? catalogFingerprint = null,
        string? sourceScenarioPath = null,
        string? sourceScenarioName = null,
        string[]? warnings = null,
        string? schemaVersion = null,
        string? generatorVersion = null)
    {
        return new SnapshotEnvelope<T>
        {
            FormatName = SnapshotConstants.FormatName,
            SchemaVersion = string.IsNullOrWhiteSpace(schemaVersion) ? SnapshotConstants.CurrentSchemaVersion : schemaVersion,
            GeneratorVersion = string.IsNullOrWhiteSpace(generatorVersion) ? SnapshotConstants.CurrentGeneratorVersion : generatorVersion,
            Metadata = new SnapshotMetadata
            {
                CatalogFingerprint = catalogFingerprint,
                SourceScenarioPath = sourceScenarioPath,
                SourceScenarioName = sourceScenarioName,
                Warnings = warnings ?? Array.Empty<string>()
            },
            Payload = payload!
        };
    }

    public void SaveSnapshot<T>(SnapshotEnvelope<T> envelope, string path, bool compress)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        _serializer.Save(envelope, path, compress);
    }

    public ImportResult<T> ImportSnapshot<T>(string path, bool skipCompatibilityCheck = false)
    {
        var envelope = _serializer.Load<SnapshotEnvelope<T>>(path);

        if (!string.Equals(envelope.FormatName, SnapshotConstants.FormatName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Snapshot format '{envelope.FormatName}' is not supported.");
        }

        var assessment = skipCompatibilityCheck
            ? new SchemaCompatibilityAssessment { Level = CompatibilityLevel.Compatible }
            : _compatibilityService.Assess(SnapshotConstants.CurrentSchemaVersion, envelope.SchemaVersion);

        if (assessment.Level == CompatibilityLevel.Incompatible)
        {
            throw new InvalidOperationException(string.Join(" ", assessment.Messages));
        }

        return new ImportResult<T>
        {
            Payload = envelope.Payload,
            Envelope = envelope,
            Compatibility = assessment
        };
    }
}
