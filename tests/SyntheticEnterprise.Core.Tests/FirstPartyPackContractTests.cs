using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;
using SyntheticEnterprise.Core.Scenarios;

namespace SyntheticEnterprise.Core.Tests;

public sealed class FirstPartyPackContractTests
{
    [Fact]
    public void Bundled_FirstParty_Packs_Satisfy_The_Pack_Contract()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var packPathResolver = services.GetRequiredService<IFirstPartyPackPathResolver>();
        var validator = services.GetRequiredService<IGenerationPluginPackageValidator>();
        var packRoots = packPathResolver.ResolvePackRootPaths();

        Assert.NotEmpty(packRoots);

        var report = Assert.Single(validator.Validate(
            packRoots,
            new ExternalPluginExecutionSettings
            {
                Enabled = true,
                PluginRootPaths = packRoots.ToList(),
                RequireAssemblyHashApproval = false
            },
            validatePackContract: true));

        Assert.True(report.PackContractChecked);
        Assert.False(report.HasErrors);
        Assert.Equal(4, report.PluginCount);
        Assert.Equal(0, report.PackContractErrorCount);
        Assert.DoesNotContain(report.PackContractIssues, issue => issue.IsError);
        Assert.Contains(report.Plugins, plugin => plugin.Capability == "FirstParty.NoOp");
        Assert.Contains(report.Plugins, plugin => plugin.Capability == "FirstParty.ITSM");
        Assert.Contains(report.Plugins, plugin => plugin.Capability == "FirstParty.SecOps");
        Assert.Contains(report.Plugins, plugin => plugin.Capability == "FirstParty.BusinessOps");
    }
}
