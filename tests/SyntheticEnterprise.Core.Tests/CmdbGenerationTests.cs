using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
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
        Assert.DoesNotContain(result.World.ConfigurationItems, item => item.CiClass == "BusinessProcessService");
        Assert.DoesNotContain(result.World.ConfigurationItems, item => item.DisplayName.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.ConfigurationItemRelationships, relationship => relationship.RelationshipType == "DependsOn");
        Assert.Contains(result.World.ConfigurationItemRelationships, relationship => relationship.RelationshipType == "InstalledOn");
        Assert.Contains(result.World.ConfigurationItemRelationships, relationship => relationship.RelationshipType == "HostedOn");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.SourceSystem == "CMDB");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.SourceSystem == "AutoDiscovery");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.SourceSystem == "ServiceCatalog");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.SourceSystem == "SpreadsheetImport");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.MatchStatus == "CatalogOnly");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.MatchStatus == "Orphaned");
        Assert.Contains(result.World.CmdbSourceRecords, record => record.CiClass == "BusinessApplication");
        Assert.Contains(result.World.ConfigurationItems, item =>
            item.CiClass == "TelephonyDevice"
            && !item.Name.StartsWith("+1-", StringComparison.OrdinalIgnoreCase)
            && !item.DisplayName.StartsWith("+1-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.ConfigurationItems, item =>
            item.CiClass == "TelephonyDevice"
            && !string.IsNullOrWhiteSpace(item.Vendor)
            && !string.IsNullOrWhiteSpace(item.Model));
        Assert.DoesNotContain(result.World.CmdbSourceRecords, record =>
            record.SourceRecordId.Contains("telephony:", StringComparison.OrdinalIgnoreCase)
            || record.SourceRecordId.Contains("device:", StringComparison.OrdinalIgnoreCase)
            || record.SourceRecordId.Contains("file-share:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.CmdbSourceRecords, record =>
            record.SourceSystem == "CMDB"
            && record.SourceRecordId.StartsWith("CI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.CmdbSourceRecords, record =>
            record.SourceSystem == "AutoDiscovery"
            && record.SourceRecordId.StartsWith("DISC-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.CmdbSourceRecords, record =>
            record.SourceSystem == "ServiceCatalog"
            && record.SourceRecordId.StartsWith("CAT-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.CmdbSourceRecords, record =>
            record.SourceSystem == "SpreadsheetImport"
            && record.SourceRecordId.StartsWith("XLS-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.CmdbSourceLinks, link => !string.IsNullOrWhiteSpace(link.ConfigurationItemId));
        Assert.Contains(result.World.CmdbSourceRelationships, relationship => relationship.RelationshipType == "InstalledOn");
        Assert.Contains(result.World.CmdbSourceRelationships, relationship => relationship.RelationshipType == "HostedOn");
        Assert.Contains(result.WorldMetadata!.AppliedLayers, layer => layer == "ConfigurationManagement");
        Assert.DoesNotContain(result.World.ConfigurationItems, item => string.IsNullOrWhiteSpace(item.BusinessCriticality));

        var criticalities = result.World.ConfigurationItems
            .Select(item => item.BusinessCriticality)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Contains("Low", criticalities);
        Assert.Contains("Medium", criticalities);
        Assert.Contains("High", criticalities);
        Assert.True(criticalities.Count >= 3);
    }

    /// <summary>
    /// A configuration management database holds more than one item for the same host or application
    /// whenever a source filed a record under an identity the database failed to match, and those
    /// items disagree about criticality because each carries the band its own source stated. Anything
    /// resolving criticality across a database therefore has a real multi-value group to resolve.
    /// </summary>
    [Fact]
    public void Some_Source_Entities_Are_Described_By_More_Than_One_Item_That_Disagrees_About_Criticality()
    {
        var world = GenerateCmdbWorld();

        var targets = world.ConfigurationItems
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceEntityType) && !string.IsNullOrWhiteSpace(item.SourceEntityId))
            .GroupBy(item => $"{item.SourceEntityType}|{item.SourceEntityId}", StringComparer.OrdinalIgnoreCase)
            .ToList();
        var multiItemTargets = targets.Where(group => group.Count() > 1).ToList();

        Assert.NotEmpty(multiItemTargets);

        var disagreeingTargets = multiItemTargets
            .Where(group => group
                .Select(item => item.BusinessCriticality)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1)
            .ToList();

        Assert.NotEmpty(disagreeingTargets);

        // Every item this generator emits still states a criticality, duplicates included.
        Assert.DoesNotContain(world.ConfigurationItems, item => string.IsNullOrWhiteSpace(item.BusinessCriticality));

        // The accidental-duplicate guard still holds: the projection describes each source entity
        // canonically once, and only an identity a source actually filed adds a further item.
        Assert.All(
            targets,
            group => Assert.Single(group, item => !item.CiKey.Contains('#', StringComparison.Ordinal)));
        Assert.Equal(
            world.ConfigurationItems.Count,
            world.ConfigurationItems.Select(item => item.CiKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// The disagreement is reported, not invented: each item's criticality is the band stated by a
    /// source record linked to that item, and the items in a disagreeing group are reached through
    /// different records.
    /// </summary>
    [Fact]
    public void A_Disagreement_About_Criticality_Traces_To_Distinct_Source_Records()
    {
        var world = GenerateCmdbWorld();
        var recordsById = world.CmdbSourceRecords.ToDictionary(record => record.Id, StringComparer.OrdinalIgnoreCase);
        var recordsByItemId = world.CmdbSourceLinks
            .GroupBy(link => link.ConfigurationItemId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Where(link => recordsById.ContainsKey(link.SourceRecordId))
                    .Select(link => recordsById[link.SourceRecordId])
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var disagreeingTargets = world.ConfigurationItems
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceEntityType) && !string.IsNullOrWhiteSpace(item.SourceEntityId))
            .GroupBy(item => $"{item.SourceEntityType}|{item.SourceEntityId}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1
                            && group.Select(item => item.BusinessCriticality).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .ToList();

        Assert.NotEmpty(disagreeingTargets);
        Assert.All(disagreeingTargets, group =>
        {
            var recordIdsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in group)
            {
                Assert.True(recordsByItemId.TryGetValue(item.Id, out var records), $"Item {item.Id} is reached by no source record.");
                Assert.Contains(
                    records!,
                    record => string.Equals(record.ObservedBusinessCriticality, item.BusinessCriticality, StringComparison.OrdinalIgnoreCase));
                Assert.All(records!, record => Assert.True(recordIdsSeen.Add(record.Id)));
            }
        });

        // The record that named the thing differently is the one the database failed to match.
        Assert.Contains(world.CmdbSourceRecords, record => record.MatchStatus == "Unreconciled");
        Assert.Contains(world.CmdbSourceLinks, link => link.LinkType == "Duplicate");
        Assert.All(
            world.CmdbSourceRecords.Where(record => record.MatchStatus == "Unreconciled"),
            record => Assert.NotEqual("CMDB", record.SourceSystem));
    }

    /// <summary>
    /// A source can also state a band the database disagrees with on a record the database did match,
    /// which is the plain observed-versus-canonical divergence the rest of this layer already models.
    /// </summary>
    [Fact]
    public void A_Matched_Source_Record_Can_State_A_Criticality_The_Item_Does_Not()
    {
        var world = GenerateCmdbWorld();
        var itemsById = world.ConfigurationItems.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        var divergent = world.CmdbSourceLinks
            .Where(link => link.LinkType == "Matched")
            .Join(world.CmdbSourceRecords, link => link.SourceRecordId, record => record.Id, (link, record) => (link, record))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.record.ObservedBusinessCriticality)
                           && itemsById.TryGetValue(pair.link.ConfigurationItemId, out var item)
                           && !string.Equals(item.BusinessCriticality, pair.record.ObservedBusinessCriticality, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(divergent);
        Assert.All(divergent, pair => Assert.NotEqual("CMDB", pair.record.SourceSystem));
    }

    private static SyntheticEnterpriseWorld GenerateCmdbWorld()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        return services.GetRequiredService<IWorldGenerator>().Generate(
            new GenerationContext
            {
                Seed = 20260923,
                GeneratedAt = new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero),
                Scenario = new ScenarioDefinition
                {
                    Name = "CMDB Multi Item",
                    IndustryProfile = "Manufacturing",
                    Cmdb = new CmdbProfile { IncludeConfigurationManagement = true },
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
                            Name = "Multi Item Manufacturing",
                            Industry = "Manufacturing",
                            EmployeeCount = 400,
                            BusinessUnitCount = 3,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            ServerCount = 20,
                            DatabaseCount = 10,
                            FileShareCount = 8,
                            CollaborationSiteCount = 10,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet()).World;
    }
}
