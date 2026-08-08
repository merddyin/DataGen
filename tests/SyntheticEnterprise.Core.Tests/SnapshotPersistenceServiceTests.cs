using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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

    [Fact]
    public void SaveSnapshot_WithExplicitReleaseIdentity_ProducesStableBytes()
    {
        var serializer = new SnapshotSerializer();
        var service = new SnapshotPersistenceService(serializer, new SchemaCompatibilityService());
        var firstPath = Path.Combine(Path.GetTempPath(), $"se-stable-first-{Guid.NewGuid():N}.json");
        var secondPath = Path.Combine(Path.GetTempPath(), $"se-stable-second-{Guid.NewGuid():N}.json");

        try
        {
            foreach (var path in new[] { firstPath, secondPath })
            {
                var envelope = service.CreateEnvelope(
                    new Dictionary<string, object> { ["WorldId"] = "world-001" },
                    sourceScenarioPath: "scenario.json",
                    sourceScenarioName: "representative-manufacturing");
                envelope.Metadata.SnapshotId = Guid.Parse("8d0b83c7-8ab0-4f83-b1a4-0ceeb1d5b0d2");
                envelope.SavedUtc = DateTime.Parse("2026-07-22T00:00:00Z").ToUniversalTime();
                service.SaveSnapshot(envelope, path, compress: false);
            }

            Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
        }
        finally
        {
            foreach (var path in new[] { firstPath, secondPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Fact]
    public void SaveSnapshot_UsesPortableScenarioProvenanceAcrossCheckoutRoots()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"se-provenance-{Guid.NewGuid():N}");
        var firstRoot = Path.Combine(tempRoot, "first-checkout");
        var secondRoot = Path.Combine(tempRoot, "second-checkout");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var firstScenario = Path.Combine(firstRoot, "scenario.json");
        var secondScenario = Path.Combine(secondRoot, "scenario.json");
        File.WriteAllText(firstScenario, "abc");
        File.WriteAllText(secondScenario, "abc");
        var firstSnapshot = Path.Combine(tempRoot, "first.seworld");
        var secondSnapshot = Path.Combine(tempRoot, "second.seworld");
        var service = new SnapshotPersistenceService(new SnapshotSerializer(), new SchemaCompatibilityService());

        try
        {
            foreach (var pair in new[]
            {
                (Scenario: firstScenario, Snapshot: firstSnapshot),
                (Scenario: secondScenario, Snapshot: secondSnapshot)
            })
            {
                var envelope = service.CreateEnvelope(
                    new Dictionary<string, object> { ["WorldId"] = "world-001" },
                    sourceScenarioPath: pair.Scenario,
                    sourceScenarioName: "representative-manufacturing");
                envelope.Metadata.SnapshotId = Guid.Parse("8d0b83c7-8ab0-4f83-b1a4-0ceeb1d5b0d2");
                envelope.SavedUtc = DateTime.Parse("2026-07-22T00:00:00Z").ToUniversalTime();
                service.SaveSnapshot(envelope, pair.Snapshot, compress: false);
            }

            Assert.Equal(File.ReadAllBytes(firstSnapshot), File.ReadAllBytes(secondSnapshot));
            using var document = JsonDocument.Parse(File.ReadAllBytes(firstSnapshot));
            var metadata = document.RootElement.GetProperty("metadata");
            Assert.Equal("scenario.json", metadata.GetProperty("sourceScenarioPath").GetString());
            Assert.Equal(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                metadata.GetProperty("sourceScenarioSha256").GetString());
            Assert.DoesNotContain(tempRoot, File.ReadAllText(firstSnapshot), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
