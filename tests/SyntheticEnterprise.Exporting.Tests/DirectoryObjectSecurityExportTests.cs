using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Profiles;
using SyntheticEnterprise.Exporting.Services;
using SyntheticEnterprise.Exporting.Writers;
using Xunit;

namespace SyntheticEnterprise.Exporting.Tests;

public sealed class DirectoryObjectSecurityExportTests
{
    [Fact]
    public void NormalizedExport_CarriesInheritanceScopeAndDaclProtection()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);
        try
        {
            var world = new SyntheticEnterpriseWorld();
            world.OrganizationalUnits.AddRange(
            [
                new DirectoryOrganizationalUnit
                {
                    Id = "OU-001",
                    CompanyId = "CO-001",
                    Name = "Endpoints",
                    DistinguishedName = "OU=Endpoints,DC=example,DC=test",
                    Purpose = "Managed Computers"
                },
                new DirectoryOrganizationalUnit
                {
                    Id = "OU-002",
                    CompanyId = "CO-001",
                    Name = "Production",
                    DistinguishedName = "OU=Production,OU=Endpoints,DC=example,DC=test",
                    ParentOuId = "OU-001",
                    Purpose = "Production Servers",
                    DaclInheritanceProtected = true
                }
            ]);
            world.AccessControlEvidence.AddRange(
            [
                new AccessControlEvidenceRecord
                {
                    Id = "ACE-001",
                    CompanyId = "CO-001",
                    PrincipalObjectId = "GRP-001",
                    PrincipalType = "Group",
                    TargetType = nameof(DirectoryOrganizationalUnit),
                    TargetId = "OU-001",
                    RightName = "GenericRead",
                    AccessType = "Allow",
                    SourceSystem = "ActiveDirectory",
                    InheritanceScope = AccessControlInheritanceScope.ThisObjectAndAllDescendants
                },
                new AccessControlEvidenceRecord
                {
                    Id = "ACE-002",
                    CompanyId = "CO-001",
                    PrincipalObjectId = "GRP-002",
                    PrincipalType = "Group",
                    TargetType = nameof(DirectoryOrganizationalUnit),
                    TargetId = "OU-001",
                    RightName = "WriteProperty",
                    AccessType = "Allow",
                    SourceSystem = "ActiveDirectory",
                    InheritanceScope = AccessControlInheritanceScope.ThisObjectOnly
                },
                new AccessControlEvidenceRecord
                {
                    Id = "ACE-003",
                    CompanyId = "CO-001",
                    PrincipalObjectId = "GRP-003",
                    PrincipalType = "Group",
                    TargetType = "Policy",
                    TargetId = "POL-001",
                    RightName = "EditSettings",
                    AccessType = "Allow",
                    SourceSystem = "ActiveDirectory"
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

            var ouArtifact = manifest.Artifacts.Single(artifact => artifact.LogicalName == "organizational_units");
            Assert.Contains("dacl_inheritance_protected", ouArtifact.Columns);

            // Appended after the columns that were already there, so a consumer reading by position
            // is unaffected.
            AssertColumnsBeginWith(
                ouArtifact.Columns,
                ["id", "company_id", "name", "distinguished_name", "parent_ou_id", "purpose", "environment_role"]);

            using var ous = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(manifest.OutputPath, ouArtifact.RelativePath)));
            var ouRows = ous.RootElement.EnumerateArray().ToArray();
            Assert.False(ouRows.Single(row => row.GetProperty("id").GetString() == "OU-001")
                .GetProperty("dacl_inheritance_protected").GetBoolean());
            Assert.True(ouRows.Single(row => row.GetProperty("id").GetString() == "OU-002")
                .GetProperty("dacl_inheritance_protected").GetBoolean());

            var evidenceArtifact = manifest.Artifacts.Single(artifact => artifact.LogicalName == "access_control_evidence");
            Assert.Contains("inheritance_scope", evidenceArtifact.Columns);
            AssertColumnsBeginWith(
                evidenceArtifact.Columns,
                [
                    "id",
                    "company_id",
                    "principal_object_id",
                    "principal_type",
                    "target_type",
                    "target_id",
                    "right_name",
                    "access_type",
                    "is_inherited",
                    "is_default_entry",
                    "source_system",
                    "inheritance_source_id",
                    "notes"
                ]);

            using var evidence = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(manifest.OutputPath, evidenceArtifact.RelativePath)));
            var evidenceRows = evidence.RootElement.EnumerateArray().ToArray();
            Assert.Equal(
                AccessControlInheritanceScope.ThisObjectAndAllDescendants,
                evidenceRows.Single(row => row.GetProperty("id").GetString() == "ACE-001")
                    .GetProperty("inheritance_scope").GetString());
            Assert.Equal(
                AccessControlInheritanceScope.ThisObjectOnly,
                evidenceRows.Single(row => row.GetProperty("id").GetString() == "ACE-002")
                    .GetProperty("inheritance_scope").GetString());

            // A policy object's access control entry has no directory-object inheritance scope, and
            // the column stays null rather than asserting one.
            Assert.Equal(
                JsonValueKind.Null,
                evidenceRows.Single(row => row.GetProperty("id").GetString() == "ACE-003")
                    .GetProperty("inheritance_scope").ValueKind);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static void AssertColumnsBeginWith(IReadOnlyList<string> columns, string[] expectedPrefix)
    {
        Assert.True(columns.Count >= expectedPrefix.Length);
        Assert.Equal(expectedPrefix, columns.Take(expectedPrefix.Length).ToArray());
    }
}
