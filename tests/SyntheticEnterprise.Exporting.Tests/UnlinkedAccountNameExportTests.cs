using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Profiles;
using SyntheticEnterprise.Exporting.Services;
using SyntheticEnterprise.Exporting.Writers;
using Xunit;

namespace SyntheticEnterprise.Exporting.Tests;

/// <summary>
/// An account can carry a given name and a surname while the directory records no employee link at
/// all. The export has to carry both facts through unchanged: the names as they stand on the object,
/// and the absent link as an absent value rather than as anything else.
/// </summary>
public sealed class UnlinkedAccountNameExportTests
{
    [Fact]
    public void NormalizedExport_CarriesNamesOfAnAccountWithNoPersonLink()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);
        try
        {
            var world = new SyntheticEnterpriseWorld();
            world.Accounts.Add(new DirectoryAccount
            {
                Id = "ACT-101",
                CompanyId = "CO-001",
                PersonId = null,
                AccountType = "Secondary",
                DisplayName = "Marta Ibarra (Secondary)",
                GivenName = "Marta",
                Surname = "Ibarra",
                Description = "Secondary account created by hand for pre-production work. "
                    + "No employee record was linked to the object when it was created.",
                SamAccountName = "lab_ibarra",
                UserPrincipalName = "marta.ibarra.lab@example.test",
                Mail = null,
                EmployeeId = null,
                ManagerAccountId = null
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

            var accountsArtifact = manifest.Artifacts.Single(artifact => artifact.LogicalName == "accounts");
            using var accounts = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(manifest.OutputPath, accountsArtifact.RelativePath)));
            var row = accounts.RootElement.EnumerateArray()
                .Single(candidate => candidate.GetProperty("id").GetString() == "ACT-101");

            Assert.Equal(JsonValueKind.Null, row.GetProperty("person_id").ValueKind);
            Assert.Equal("Marta", row.GetProperty("given_name").GetString());
            Assert.Equal("Ibarra", row.GetProperty("surname").GetString());
            Assert.Equal("Marta Ibarra (Secondary)", row.GetProperty("display_name").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("employee_id").ValueKind);
            Assert.Contains(
                "No employee record was linked",
                row.GetProperty("description").GetString()!,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }
}
