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
            Assert.Equal("Current", management.RootElement[0].GetProperty("lifecycle_state").GetString());
            Assert.True(management.RootElement[0].GetProperty("is_current").GetBoolean());
            Assert.Equal(JsonValueKind.Null, management.RootElement[0].GetProperty("superseded_by_observation_id").ValueKind);
            Assert.Equal("Removed", relationships.RootElement[0].GetProperty("lifecycle_state").GetString());
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void NormalizedExport_DeclaresCurrentSelectionPerCompanyEndpointAndProviderRegardlessOfRowOrder()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);
        try
        {
            var currentA = CreateCurrentObservation(
                id: "MGO-100",
                companyId: "CO-001",
                provider: "RepresentativeProviderA",
                observedAtUtc: DateTimeOffset.Parse("2026-07-22T00:00:00Z"));
            var historicalA = currentA with
            {
                Id = "MGO-900",
                LifecycleState = "Historical",
                IsCurrent = false,
                SupersededByObservationId = currentA.Id,
                RegistrationState = "Unreachable",
                DeploymentCapability = "Unknown",
                ObservedAtUtc = DateTimeOffset.Parse("2026-06-25T00:00:00Z"),
                LastCheckInAtUtc = DateTimeOffset.Parse("2026-06-07T00:00:00Z"),
            };
            var currentB = CreateCurrentObservation(
                id: "MGO-800",
                companyId: "CO-002",
                provider: "RepresentativeProviderB",
                observedAtUtc: DateTimeOffset.Parse("2026-07-22T00:00:00Z"));
            var historicalB = currentB with
            {
                Id = "MGO-050",
                LifecycleState = "Historical",
                IsCurrent = false,
                SupersededByObservationId = currentB.Id,
                RegistrationState = "Unreachable",
                DeploymentCapability = "Unknown",
                ObservedAtUtc = DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
                LastCheckInAtUtc = DateTimeOffset.Parse("2026-06-10T00:00:00Z"),
            };
            var world = new SyntheticEnterpriseWorld();
            world.ManagementObservations.AddRange([historicalA, currentB, currentA, historicalB]);

            var coordinator = CreateCoordinator();
            var manifest = coordinator.Export(new GenerationResult
            {
                World = world,
                Statistics = new GenerationStatistics(),
            }, CreateRequest(temp));

            var artifact = manifest.Artifacts.Single(candidate =>
                candidate.LogicalName == "endpoint_management_observations");
            Assert.Equal(4, artifact.RowCount);
            Assert.Contains("lifecycle_state", artifact.Columns);
            Assert.Contains("is_current", artifact.Columns);
            Assert.Contains("superseded_by_observation_id", artifact.Columns);
            using var document = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(manifest.OutputPath, artifact.RelativePath)));
            var rows = document.RootElement.EnumerateArray()
                .Select((row, index) => new ExportedManagementObservation(
                    Index: index,
                    Id: row.GetProperty("id").GetString()!,
                    CompanyId: row.GetProperty("company_id").GetString()!,
                    EndpointType: row.GetProperty("endpoint_type").GetString()!,
                    EndpointId: row.GetProperty("endpoint_id").GetString()!,
                    Provider: row.GetProperty("management_provider").GetString()!,
                    RegistrationId: row.GetProperty("registration_id").GetString(),
                    AgentInstanceId: row.GetProperty("agent_instance_id").GetString(),
                    LifecycleState: row.GetProperty("lifecycle_state").GetString()!,
                    IsCurrent: row.GetProperty("is_current").GetBoolean(),
                    SupersededByObservationId: row.GetProperty("superseded_by_observation_id").GetString()))
                .ToArray();

            Assert.Equal(rows.Length, rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
            var scopes = rows.GroupBy(row => new
            {
                row.CompanyId,
                row.EndpointType,
                row.EndpointId,
                row.Provider,
            }).ToArray();
            Assert.Equal(2, scopes.Length);
            Assert.All(scopes, scope =>
            {
                var current = Assert.Single(scope, row => row.IsCurrent);
                var historical = Assert.Single(scope, row => !row.IsCurrent);
                Assert.Equal("Current", current.LifecycleState);
                Assert.Null(current.SupersededByObservationId);
                Assert.Equal("Historical", historical.LifecycleState);
                Assert.Equal(current.Id, historical.SupersededByObservationId);
                Assert.Equal(current.RegistrationId, historical.RegistrationId);
                Assert.Equal(current.AgentInstanceId, historical.AgentInstanceId);
            });
            Assert.True(rows.Single(row => row.Id == historicalA.Id).Index > rows.Single(row => row.Id == currentA.Id).Index);
            Assert.True(rows.Single(row => row.Id == historicalB.Id).Index < rows.Single(row => row.Id == currentB.Id).Index);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static EndpointManagementObservation CreateCurrentObservation(
        string id,
        string companyId,
        string provider,
        DateTimeOffset observedAtUtc)
        => new()
        {
            Id = id,
            CompanyId = companyId,
            EndpointType = "Device",
            EndpointId = "SHARED-ENDPOINT-001",
            ObservationKind = "Registration",
            SourceKind = "GeneratedInventory",
            ManagementProvider = provider,
            AgentInstanceId = "agent-SHARED-ENDPOINT-001",
            RegistrationId = "registration-SHARED-ENDPOINT-001",
            RegistrationState = "Registered",
            ConfigurationCapability = "Supported",
            DeploymentCapability = "Supported",
            UpdateCapability = "Supported",
            ObservedAtUtc = observedAtUtc,
            LastCheckInAtUtc = observedAtUtc.AddHours(-2),
            LifecycleState = "Current",
            IsCurrent = true,
        };

    private static WorldExportCoordinator CreateCoordinator()
        => new(
            new NormalizedEntityTableProvider(),
            new NormalizedLinkTableProvider(),
            new JsonArtifactWriter(),
            new ExportManifestBuilder(),
            new ExportSummaryBuilder(),
            new ExportPathResolver());

    private static ExportRequest CreateRequest(string outputPath)
        => new()
        {
            Format = ExportSerializationFormat.Json,
            Profile = ExportProfileKind.Normalized,
            OutputPath = outputPath,
            IncludeManifest = true,
            IncludeSummary = false,
            Overwrite = true,
        };

    private sealed record ExportedManagementObservation(
        int Index,
        string Id,
        string CompanyId,
        string EndpointType,
        string EndpointId,
        string Provider,
        string? RegistrationId,
        string? AgentInstanceId,
        string LifecycleState,
        bool IsCurrent,
        string? SupersededByObservationId);
}
