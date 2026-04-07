using SyntheticEnterprise.Core.Contracts;

namespace SyntheticEnterprise.Core.Services;

public interface ISnapshotPersistenceService
{
    SnapshotEnvelope<T> CreateEnvelope<T>(
        T payload,
        CatalogContentFingerprint? catalogFingerprint = null,
        string? sourceScenarioPath = null,
        string? sourceScenarioName = null,
        string[]? warnings = null,
        string? schemaVersion = null,
        string? generatorVersion = null);

    void SaveSnapshot<T>(SnapshotEnvelope<T> envelope, string path, bool compress);

    ImportResult<T> ImportSnapshot<T>(string path, bool skipCompatibilityCheck = false);
}
