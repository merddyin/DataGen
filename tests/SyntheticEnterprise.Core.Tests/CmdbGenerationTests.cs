using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class CmdbGenerationTests
{
    [Fact]
    public void WorldGenerator_DoesNotGenerate_Cmdb_When_Disabled()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "CMDB Disabled",
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "CMDB Disabled Co",
                            Industry = "Technology",
                            EmployeeCount = 250,
                            BusinessUnitCount = 3,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet());

        Assert.Empty(result.World.ConfigurationItems);
        Assert.Empty(result.World.ConfigurationItemRelationships);
        Assert.Empty(result.World.CmdbSourceRecords);
        Assert.Empty(result.World.CmdbSourceLinks);
        Assert.Empty(result.World.CmdbSourceRelationships);
    }

    [Fact]
    public void WorldGenerator_Generates_Cmdb_Canonical_And_Source_Data_When_Enabled()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "CMDB Enabled",
                    IndustryProfile = "Manufacturing",
                    DeviationProfile = ScenarioDeviationProfiles.Aggressive,
                    EmployeeSize = new SizeBand { Minimum = 900, Maximum = 1200 },
                    Cmdb = new CmdbProfile
                    {
                        IncludeConfigurationManagement = true,
                        IncludeBusinessServices = true,
                        IncludeCloudServices = true,
                        IncludeAutoDiscoveryRecords = true,
                        IncludeServiceCatalogRecords = true,
                        IncludeSpreadsheetImportRecords = true
                    },
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 6,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "CMDB Enabled Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 1000,
                            BusinessUnitCount = 4,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 3,
                            DatabaseCount = 12,
                            FileShareCount = 8,
                            CollaborationSiteCount = 10,
                            ServerCount = 20,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet());

        Assert.NotEmpty(result.World.ConfigurationItems);
        Assert.NotEmpty(result.World.ConfigurationItemRelationships);
        Assert.NotEmpty(result.World.CmdbSourceRecords);
        Assert.NotEmpty(result.World.CmdbSourceLinks);
        Assert.NotEmpty(result.World.CmdbSourceRelationships);
        Assert.Contains(result.World.ConfigurationItems, item => item.CiType == "Application");
        Assert.Contains(result.World.ConfigurationItems, item => item.CiType == "Platform");
        Assert.Contains(result.World.ConfigurationItems, item => item.CiClass == "Server");
        Assert.Contains(result.World.ConfigurationItems, item => item.CiClass == "InstalledSoftware");
        Assert.Contains(result.World.ConfigurationItems, item => item.CiClass == "CollaborationWorkspace");
        Assert.Contains(result.World.ConfigurationItemRelationships, relationship => relationship.RelationshipType == "DependsOn");
        Assert.Contains(result.World.ConfigurationItemRelationships, relationship => relationship.RelationshipType == "InstalledOn");
        Assert.Contains(result.World.ConfigurationItemRelationships, relationship => relationship.RelationshipType == "HostedOn");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.SourceSystem == "CMDB");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.SourceSystem == "AutoDiscovery");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.SourceSystem == "ServiceCatalog");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.SourceSystem == "SpreadsheetImport");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.MatchStatus == "CatalogOnly");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.MatchStatus == "Orphaned");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.CiType == "Platform" && record.CiClass == "BusinessApplication");
        Assert.Contains(result.World.CmdbSourceLinks, link => !string.IsNullOrWhiteSpace(link.ConfigurationItemId));
        Assert.Contains(result.World.CmdbSourceRelationships, relationship => relationship.RelationshipType == "InstalledOn");
        Assert.Contains(result.World.CmdbSourceRelationships, relationship => relationship.RelationshipType == "HostedOn");
        Assert.Contains(result.WorldMetadata!.AppliedLayers, layer => layer == "ConfigurationManagement");
    }
}
