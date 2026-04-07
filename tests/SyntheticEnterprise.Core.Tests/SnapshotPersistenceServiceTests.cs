using System;
using System.Collections.Generic;
using System.IO;
using SyntheticEnterprise.Core.Contracts;
using SyntheticEnterprise.Core.Serialization;
using SyntheticEnterprise.Core.Services;
using Xunit;

namespace SyntheticEnterprise.Core.Tests;

public sealed class SnapshotPersistenceServiceTests
{
    [Fact]
    public void Save_and_import_roundtrip_preserves_payload_and_metadata()
    {
        var serializer = new SnapshotSerializer();
        var compatibility = new SchemaCompatibilityService();
        var service = new SnapshotPersistenceService(serializer, compatibility);

        var payload = new Dictionary<string, object>
        {
            ["WorldId"] = "world-001",
            ["CompanyCount"] = 3
        };

        var fingerprint = new CatalogContentFingerprint
        {
            RootPath = "catalogs",
            AggregateSha256 = "ABC123"
        };

        var envelope = service.CreateEnvelope(
            payload,
            catalogFingerprint: fingerprint,
            sourceScenarioPath: "examples/regional_manufacturer.scenario.json",
            sourceScenarioName: "regional_manufacturer");

        var path = Path.Combine(Path.GetTempPath(), $"se-roundtrip-{Guid.NewGuid():N}.json");

        try
        {
            service.SaveSnapshot(envelope, path, compress: false);
            var imported = service.ImportSnapshot<Dictionary<string, object>>(path);

            Assert.Equal(CompatibilityLevel.Compatible, imported.Compatibility.Level);
            Assert.Equal("world-001", imported.Payload["WorldId"].ToString());
            Assert.Equal("regional_manufacturer", imported.Envelope.Metadata.SourceScenarioName);
            Assert.NotNull(imported.Envelope.Metadata.CatalogFingerprint);
            Assert.Equal("ABC123", imported.Envelope.Metadata.CatalogFingerprint!.AggregateSha256);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
