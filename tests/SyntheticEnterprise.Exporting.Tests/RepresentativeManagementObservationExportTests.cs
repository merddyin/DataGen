using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Profiles;
using SyntheticEnterprise.Exporting.Services;
using SyntheticEnterprise.Exporting.Writers;
using Xunit;

namespace SyntheticEnterprise.Exporting.Tests;

public sealed class RepresentativeManagementObservationExportTests
{
    [Fact]
    public void NormalizedExport_WritesManagementAndRelationshipObservationArtifacts()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);
        try
        {
            var world = new SyntheticEnterpriseWorld();
            world.ManagementObservations.Add(new EndpointManagementObservation
            {
                Id = "MGO-001",
                CompanyId = "CO-001",
                EndpointType = "Device",
                EndpointId = "DEV-001",
                DeviceAccountId = "ACC-001",
                ObservationKind = "Registration",
                SourceKind = "DataGenCore",
                ManagementProvider = "ConfigurationManager",
                RegistrationState = "Registered",
                JoinState = "HybridJoined",
                ConfigurationCapability = "Supported",
                DeploymentCapability = "Supported",
                UpdateCapability = "Supported",
                OperatingSystemFamily = "Windows",
                Cohort = "windows-common",
                ObservedAtUtc = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
                LastCheckInAtUtc = DateTimeOffset.Parse("2026-07-21T00:00:00Z"),
                Confidence = .96m,
            });
            world.RelationshipHistoryObservations.Add(new RelationshipHistoryObservation
            {
                Id = "RHO-001",
                CompanyId = "CO-001",
                RelationshipType = "Owns",
                FromArtifact = "people",
                FromEntityType = "Person",
                FromEntityId = "PER-001",
                ToArtifact = "software_packages",
                ToEntityType = "Application",
                ToEntityId = "SW-001",
                LifecycleState = "Removed",
                SourceSystem = "ServiceCatalog",
                ObservedAtUtc = DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                RemovedAtUtc = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            });

            var coordinator = new WorldExportCoordinator(
                new NormalizedEntityTableProvider(),
                new NormalizedLinkTableProvider(),
                new JsonArtifactWriter(),
                new ExportManifestBuilder(),
                new ExportSummaryBuilder(),
                new ExportPathResolver());
            var manifest = coordinator.Export(new GenerationResult
            {
                World = world,
                Statistics = new GenerationStatistics(),
            }, new ExportRequest
            {
                Format = ExportSerializationFormat.Json,
                Profile = ExportProfileKind.Normalized,
                OutputPath = temp,
                IncludeManifest = true,
                IncludeSummary = false,
                Overwrite = true,
            });

            Assert.Contains(manifest.Artifacts, artifact =>
                artifact.LogicalName == "endpoint_management_observations"
                && artifact.RowCount == 1);
            Assert.Contains(manifest.Artifacts, artifact =>
                artifact.LogicalName == "relationship_history_observations"
                && artifact.RowCount == 1);

            var managementArtifact = manifest.Artifacts.Single(artifact =>
                artifact.LogicalName == "endpoint_management_observations");
            var relationshipArtifact = manifest.Artifacts.Single(artifact =>
                artifact.LogicalName == "relationship_history_observations");
            using var management = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(manifest.OutputPath, managementArtifact.RelativePath)));
            using var relationships = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(manifest.OutputPath, relationshipArtifact.RelativePath)));
            Assert.Equal("Registered", management.RootElement[0].GetProperty("registration_state").GetString());
            Assert.Equal("Removed", relationships.RootElement[0].GetProperty("lifecycle_state").GetString());
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }
}
