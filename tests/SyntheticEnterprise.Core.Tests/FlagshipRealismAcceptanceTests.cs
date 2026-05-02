using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class FlagshipRealismAcceptanceTests
{
    public static TheoryData<string, int, int, int, int> ScenarioCases => new()
    {
        { Path.Combine(TestEnvironmentPaths.GetRepositoryRoot(), "examples", "regional_manufacturer.scenario.json"), 4242, 100, 70, 900 },
        { Path.Combine(TestEnvironmentPaths.GetRepositoryRoot(), "examples", "regional_manufacturer.scenario.json"), 5151, 60, 60, 700 },
        { Path.Combine(TestEnvironmentPaths.GetRepositoryRoot(), "examples", "regional_manufacturer.scenario.json"), 4242, 60, 60, 700 },
        { Path.Combine(TestEnvironmentPaths.GetRepositoryRoot(), "examples", "regional_manufacturer.scenario.json"), 7777, 60, 60, 700 }
    };

    [Theory]
    [MemberData(nameof(ScenarioCases))]
    public void Flagship_Scenarios_Meet_Realism_Acceptance_Floors(string scenarioPath, int seed, int minimumGroups, int minimumPolicies, int minimumPolicySettings)
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var catalogLoader = services.GetRequiredService<ICatalogLoader>();
        var scenarioLoader = services.GetRequiredService<IScenarioLoader>();
        var generator = services.GetRequiredService<IWorldGenerator>();
        var auditService = services.GetRequiredService<IWorldQualityAuditService>();

        var catalogs = catalogLoader.LoadDefault();
        var scenario = scenarioLoader.LoadFromPath(scenarioPath);
        var result = generator.Generate(new GenerationContext
        {
            Scenario = scenario,
            Seed = seed
        }, catalogs);
        var audit = auditService.Audit(result.World);

        Assert.True(result.World.Groups.Count >= minimumGroups, $"Expected at least {minimumGroups} groups, found {result.World.Groups.Count}.");
        Assert.True(result.World.Policies.Count >= minimumPolicies, $"Expected at least {minimumPolicies} policies, found {result.World.Policies.Count}.");
        Assert.True(result.World.PolicySettings.Count >= minimumPolicySettings, $"Expected at least {minimumPolicySettings} policy settings, found {result.World.PolicySettings.Count}.");

        Assert.Equal(0, audit.Metrics["duplicate_person_upns"]);
        Assert.Equal(0, audit.Metrics["duplicate_account_upns"]);
        Assert.Equal(0, audit.Metrics["duplicate_account_sam_account_names"]);
        Assert.Equal(0, audit.Metrics["duplicate_person_display_names"]);
        Assert.True(audit.Metrics["max_person_display_name_repeat"] <= 1, $"Expected person display names to remain unique, found max repeat of {audit.Metrics["max_person_display_name_repeat"]}.");
        Assert.Equal(0, audit.Metrics["duplicate_application_names"]);
        Assert.Equal(0, audit.Metrics["duplicate_application_service_names"]);
        Assert.Equal(0, audit.Metrics["duplicate_group_names"]);
        Assert.Equal(0, audit.Metrics["duplicate_identity_store_names"]);
        Assert.Equal(0, audit.Metrics["duplicate_device_hostnames"]);
        Assert.Equal(0, audit.Metrics["duplicate_server_hostnames"]);
        Assert.Equal(0, audit.Metrics["duplicate_network_asset_hostnames"]);
        Assert.Equal(0, audit.Metrics["duplicate_configuration_item_display_names"]);
        Assert.Equal(0, audit.Metrics["duplicate_collaboration_site_names"]);
        Assert.Equal(0, audit.Metrics["numbered_collaboration_site_names"]);
        Assert.Equal(0, audit.Metrics["accounts_missing_temporal_identity_evidence"]);
        Assert.Equal(0, audit.Metrics["workstations_missing_identity_evidence"]);
        Assert.Equal(0, audit.Metrics["servers_missing_directory_account_evidence"]);
        Assert.Equal(0, audit.Metrics["duplicate_business_unit_names"]);
        Assert.Equal(0, audit.Metrics["duplicate_department_names"]);
        Assert.Equal(0, audit.Metrics["duplicate_team_names"]);
        Assert.Equal(0, audit.Metrics["numbered_external_org_names"]);
        Assert.Equal(0, audit.Metrics["numbered_business_unit_names"]);
        Assert.Equal(0, audit.Metrics["numbered_department_names"]);
        Assert.Equal(0, audit.Metrics["numbered_team_names"]);
        Assert.Equal(0, audit.Metrics["generic_share_names"]);
        Assert.Equal(0, audit.Metrics["generic_folder_names"]);
        Assert.Equal(0, audit.Metrics["generic_channel_names"]);
        Assert.Equal(0, audit.Metrics["business_process_configuration_items"]);
        Assert.Equal(0, audit.Metrics["undersized_policy_surface"]);
        Assert.Equal(0, audit.Metrics["policies_missing_guid"]);
        Assert.Equal(0, audit.Metrics["policy_settings_missing_path"]);
        Assert.Equal(0, audit.Metrics["group_policies_without_linked_container"]);
        Assert.Equal(0, audit.Metrics["intune_policies_without_scope_or_assignment"]);
        Assert.Equal(0, audit.Metrics["conditional_access_policies_without_scope_or_assignment"]);

        Assert.NotEmpty(audit.Samples["business_units"]);
        Assert.NotEmpty(audit.Samples["departments"]);
        Assert.NotEmpty(audit.Samples["teams"]);
        Assert.NotEmpty(audit.Samples["file_shares"]);
        Assert.NotEmpty(audit.Samples["collaboration_sites"]);
        Assert.NotEmpty(audit.Samples["document_libraries"]);
        Assert.NotEmpty(audit.Samples["groups"]);
        Assert.NotEmpty(audit.Samples["policies"]);
        Assert.NotEmpty(audit.Samples["policy_settings"]);
        Assert.NotEmpty(audit.Samples["applications"]);
        Assert.NotEmpty(audit.Samples["application_services"]);
        Assert.NotEmpty(audit.Samples["network_assets"]);
        Assert.NotEmpty(audit.Samples["external_organizations"]);
        Assert.NotEmpty(audit.Samples["people"]);
        Assert.NotEmpty(audit.Samples["accounts"]);
    }

    [Fact]
    public void Flagship_External_Workforce_Scenario_Maintains_Unique_Display_Names()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var catalogLoader = services.GetRequiredService<ICatalogLoader>();
        var generator = services.GetRequiredService<IWorldGenerator>();
        var auditService = services.GetRequiredService<IWorldQualityAuditService>();

        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 4242,
                Scenario = new ScenarioDefinition
                {
                    Name = "Flagship External Workforce Acceptance",
                    IndustryProfile = "Manufacturing",
                    GeographyProfile = "North-America",
                    Identity = new IdentityProfile
                    {
                        IncludeHybridDirectory = true,
                        IncludeM365StyleGroups = true,
                        IncludeAdministrativeTiers = true,
                        IncludeExternalWorkforce = true,
                        IncludeB2BGuests = true,
                        ContractorRatio = 0.10,
                        ManagedServiceProviderRatio = 0.03,
                        GuestUserRatio = 0.08,
                        StaleAccountRate = 0.04
                    },
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Flagship External Workforce Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 5000,
                            BusinessUnitCount = 6,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 6,
                            SharedMailboxCount = 18,
                            ServiceAccountCount = 30,
                            IncludePrivilegedAccounts = true,
                            Countries = { "United States", "Canada", "Mexico" }
                        }
                    }
                }
            },
            catalogLoader.LoadDefault());
        var audit = auditService.Audit(result.World);

        Assert.Equal(0, audit.Metrics["duplicate_person_display_names"]);
        Assert.True(audit.Metrics["max_person_display_name_repeat"] <= 1);
        Assert.Equal(0, audit.Metrics["duplicate_person_upns"]);
        Assert.Equal(0, audit.Metrics["duplicate_account_upns"]);
        Assert.Equal(0, audit.Metrics["duplicate_account_sam_account_names"]);
        Assert.DoesNotContain(
            audit.Warnings,
            warning => warning.Contains("duplicate person display names", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Flagship_Large_Internal_Workforce_Scenario_Maintains_Unique_Display_Names()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var catalogLoader = services.GetRequiredService<ICatalogLoader>();
        var generator = services.GetRequiredService<IWorldGenerator>();
        var auditService = services.GetRequiredService<IWorldQualityAuditService>();

        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 4242,
                Scenario = new ScenarioDefinition
                {
                    Name = "Flagship Large Internal Workforce Acceptance",
                    IndustryProfile = "Manufacturing",
                    GeographyProfile = "North-America",
                    Identity = new IdentityProfile
                    {
                        IncludeHybridDirectory = true,
                        IncludeM365StyleGroups = true,
                        IncludeAdministrativeTiers = true,
                        IncludeExternalWorkforce = false,
                        IncludeB2BGuests = false,
                        StaleAccountRate = 0.04
                    },
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Flagship Internal Workforce Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 15000,
                            BusinessUnitCount = 8,
                            DepartmentCountPerBusinessUnit = 5,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 10,
                            SharedMailboxCount = 30,
                            ServiceAccountCount = 45,
                            IncludePrivilegedAccounts = true,
                            Countries = { "United States", "Canada", "Mexico" }
                        }
                    }
                }
            },
            catalogLoader.LoadDefault());
        var audit = auditService.Audit(result.World);

        Assert.Equal(0, audit.Metrics["duplicate_person_display_names"]);
        Assert.True(audit.Metrics["max_person_display_name_repeat"] <= 1);
        Assert.Equal(0, audit.Metrics["duplicate_person_upns"]);
        Assert.Equal(0, audit.Metrics["duplicate_account_upns"]);
        Assert.Equal(0, audit.Metrics["duplicate_account_sam_account_names"]);
        Assert.DoesNotContain(
            audit.Warnings,
            warning => warning.Contains("duplicate person display names", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duckburg_Subset_Scenario_Is_Deterministic_For_Same_Seed_Across_Repeated_Generations()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var catalogLoader = services.GetRequiredService<ICatalogLoader>();
        var scenarioLoader = services.GetRequiredService<IScenarioLoader>();
        var generator = services.GetRequiredService<IWorldGenerator>();

        var catalogs = catalogLoader.LoadDefault();
        var scenario = scenarioLoader.LoadFromPath(Path.Combine(
            TestEnvironmentPaths.GetRepositoryRoot(),
            "tests",
            "SyntheticEnterprise.Core.Tests",
            "TestData",
            "duckburg-subset.scenario.json"));

        var first = generator.Generate(new GenerationContext
        {
            Scenario = scenario,
            Seed = 4242
        }, catalogs);
        var second = generator.Generate(new GenerationContext
        {
            Scenario = scenario,
            Seed = 4242
        }, catalogs);
        var third = generator.Generate(new GenerationContext
        {
            Scenario = scenario,
            Seed = 4242
        }, catalogs);

        Assert.Equal(first.Statistics.PersonCount, second.Statistics.PersonCount);
        Assert.Equal(first.Statistics.PersonCount, third.Statistics.PersonCount);
        Assert.Equal(first.Statistics.AccountCount, second.Statistics.AccountCount);
        Assert.Equal(first.Statistics.AccountCount, third.Statistics.AccountCount);
        Assert.Equal(first.World.Devices.Count, second.World.Devices.Count);
        Assert.Equal(first.World.Devices.Count, third.World.Devices.Count);
        Assert.Equal(first.World.Servers.Count, second.World.Servers.Count);
        Assert.Equal(first.World.Servers.Count, third.World.Servers.Count);
        Assert.Equal(first.World.ExternalOrganizations.Count, second.World.ExternalOrganizations.Count);
        Assert.Equal(first.World.ExternalOrganizations.Count, third.World.ExternalOrganizations.Count);

        var firstNames = first.World.ExternalOrganizations.Select(organization => organization.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var secondNames = second.World.ExternalOrganizations.Select(organization => organization.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var thirdNames = third.World.ExternalOrganizations.Select(organization => organization.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.Equal(firstNames, secondNames);
        Assert.Equal(firstNames, thirdNames);
        Assert.Equal(first.Warnings, second.Warnings);
        Assert.Equal(first.Warnings, third.Warnings);
    }
}
