using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Profiles;
using SyntheticEnterprise.Exporting.Services;
using SyntheticEnterprise.Exporting.Writers;
using Xunit;

namespace SyntheticEnterprise.Exporting.Tests;

public sealed class AccountNameAndDescriptionExportTests
{
    [Fact]
    public void NormalizedExport_CarriesRawAccountNamesAndDescription()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);
        try
        {
            var world = new SyntheticEnterpriseWorld();
            world.Accounts.AddRange(
            [
                new DirectoryAccount
                {
                    Id = "ACT-001",
                    CompanyId = "CO-001",
                    PersonId = "PER-001",
                    AccountType = "User",
                    DisplayName = "Rowan Vance",
                    GivenName = "Rowan",
                    Surname = "Vance",
                    Description = "Primary user account for Rowan Vance, Logistics Analyst.",
                    SamAccountName = "rvance00001",
                    UserPrincipalName = "rowan.vance@example.test",
                    Mail = "rowan.vance@example.test"
                },
                new DirectoryAccount
                {
                    Id = "ACT-002",
                    CompanyId = "CO-001",
                    AccountType = "Service",
                    DisplayName = "svc-sql-database-01",
                    GivenName = null,
                    Surname = null,
                    Description = "Service account for the sql database workload (svc-sql-database-01).",
                    SamAccountName = "svc_sql_dat01",
                    UserPrincipalName = "svc.sql.database.01@example.test"
                }
            ]);

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

            var accountsArtifact = manifest.Artifacts.Single(artifact => artifact.LogicalName == "accounts");
            Assert.Contains("given_name", accountsArtifact.Columns);
            Assert.Contains("surname", accountsArtifact.Columns);
            Assert.Contains("description", accountsArtifact.Columns);

            using var accounts = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(manifest.OutputPath, accountsArtifact.RelativePath)));
            var rows = accounts.RootElement.EnumerateArray().ToArray();

            var personRow = rows.Single(row => row.GetProperty("id").GetString() == "ACT-001");
            Assert.Equal("Rowan", personRow.GetProperty("given_name").GetString());
            Assert.Equal("Vance", personRow.GetProperty("surname").GetString());
            Assert.Equal(
                "Primary user account for Rowan Vance, Logistics Analyst.",
                personRow.GetProperty("description").GetString());

            var serviceRow = rows.Single(row => row.GetProperty("id").GetString() == "ACT-002");
            Assert.Equal(JsonValueKind.Null, serviceRow.GetProperty("given_name").ValueKind);
            Assert.Equal(JsonValueKind.Null, serviceRow.GetProperty("surname").ValueKind);
            Assert.Equal(
                "Service account for the sql database workload (svc-sql-database-01).",
                serviceRow.GetProperty("description").GetString());
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }
}
