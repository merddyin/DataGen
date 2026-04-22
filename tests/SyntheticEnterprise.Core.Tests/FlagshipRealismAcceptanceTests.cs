using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class FlagshipRealismAcceptanceTests
{
    public static TheoryData<string, int, int, int, int> ScenarioCases => new()
    {
        { Path.Combine(TestEnvironmentPaths.GetRepositoryRoot(), "artifacts", "duckburg-subset.scenario.json"), 4242, 100, 70, 900 },
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
        Assert.Equal(0, audit.Metrics["numbered_business_unit_names"]);
        Assert.Equal(0, audit.Metrics["numbered_department_names"]);
        Assert.Equal(0, audit.Metrics["numbered_team_names"]);
        Assert.Equal(0, audit.Metrics["generic_share_names"]);
        Assert.Equal(0, audit.Metrics["generic_folder_names"]);
        Assert.Equal(0, audit.Metrics["generic_channel_names"]);
        Assert.Equal(0, audit.Metrics["business_process_configuration_items"]);
        Assert.Equal(0, audit.Metrics["undersized_policy_surface"]);

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
    }
}
