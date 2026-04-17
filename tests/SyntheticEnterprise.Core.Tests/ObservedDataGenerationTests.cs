using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.Catalogs;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class ObservedDataGenerationTests
{
    [Fact]
    public void WorldGenerator_Populates_Observed_Views_When_Enabled()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 1,
                Scenario = new ScenarioDefinition
                {
                    Name = "Observed Views Test",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 6,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    ObservedData = new ObservedDataProfile
                    {
                        IncludeObservedViews = true,
                        CoverageRatio = 0.75
                    },
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Observed Views Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 500,
                            BusinessUnitCount = 3,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 3,
                            Countries = { "United States" }
                        }
                    }
                }
            },
            new CatalogSet());

        Assert.NotEmpty(result.World.ObservedEntitySnapshots);
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.EntityType == "Account");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.EntityType == "Device");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.EntityType == "Application");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.EntityType == "ApplicationService");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.EntityType == "CloudTenant");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.EntityType == "CrossTenantPolicy");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.EntityType == "CrossTenantGuestAccess");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.EntityType == "EndpointPolicy");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.EntityType == "EndpointLocalGroup");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.DriftType != "None");
        Assert.Contains(result.World.CrossTenantAccessPolicies, policy => !string.IsNullOrWhiteSpace(policy.ResourceTenantDomain));
        Assert.Contains(result.World.CrossTenantAccessEvents, accessEvent => accessEvent.SourceSystem == "Entra ID");
        Assert.Contains(result.World.CrossTenantAccessEvents, accessEvent => accessEvent.EventType == "AccessPackageAssigned");
        Assert.Contains(result.World.CrossTenantAccessEvents, accessEvent => accessEvent.EventType == "AccessReviewCompleted");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot => snapshot.SourceSystem == "Entra ID" && snapshot.GroundTruthState.Contains("Guest", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "CloudTenant"
            && snapshot.SourceSystem.Contains("Admin", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.WorldMetadata!.AppliedLayers, layer => layer == "ObservedViews");
    }

    [Fact]
    public void WorldGenerator_Uses_Curated_Observed_Source_Patterns_When_Catalogs_Are_Loaded()
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
                    Name = "Observed Catalog Views Test",
                    IndustryProfile = "Manufacturing",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 6,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    ObservedData = new ObservedDataProfile
                    {
                        IncludeObservedViews = true,
                        CoverageRatio = 0.8
                    },
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Observed Catalog Views Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 2200,
                            BusinessUnitCount = 4,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 4,
                            Countries = { "United States" }
                        }
                    }
                }
            },
            new FileSystemCatalogLoader().LoadFromPath(TestEnvironmentPaths.GetCatalogRoot()));

        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "Application"
            && snapshot.DisplayName.Contains("Workday", StringComparison.OrdinalIgnoreCase)
            && snapshot.SourceSystem == "Workday Application Inventory");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "ApplicationService"
            && snapshot.DisplayName.Contains("Databricks", StringComparison.OrdinalIgnoreCase)
            && snapshot.DisplayName.Contains("Data Access", StringComparison.OrdinalIgnoreCase)
            && snapshot.SourceSystem == "Databricks Jobs and Clusters");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "CloudTenant"
            && snapshot.DisplayName.Contains("Databricks", StringComparison.OrdinalIgnoreCase)
            && snapshot.SourceSystem == "Databricks Account Console");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "FileShare"
            && snapshot.GroundTruthState == "Department"
            && snapshot.SourceSystem == "File Server Resource Manager");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "CollaborationSite"
            && snapshot.SourceSystem is "Teams Admin Center" or "SharePoint Admin Center");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "Account"
            && snapshot.GroundTruthState.Contains("Guest", StringComparison.OrdinalIgnoreCase)
            && snapshot.SourceSystem == "Entra B2B Collaboration");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "Device"
            && snapshot.SourceSystem == "Intune Device Inventory");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "Server"
            && snapshot.SourceSystem == "Service Configuration Inventory");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "EndpointPolicy"
            && snapshot.SourceSystem is "Intune Policy Compliance" or "Group Policy Results");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "EndpointLocalGroup"
            && snapshot.SourceSystem is "Defender for Endpoint Local Admin Inventory" or "Server Local Admin Inventory");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "CrossTenantPolicy"
            && snapshot.SourceSystem == "Entra Cross-Tenant Policy Inventory");
        Assert.Contains(result.World.ObservedEntitySnapshots, snapshot =>
            snapshot.EntityType == "CrossTenantGuestAccess"
            && snapshot.SourceSystem == "Entra Entitlement Management Reports");
    }
}
