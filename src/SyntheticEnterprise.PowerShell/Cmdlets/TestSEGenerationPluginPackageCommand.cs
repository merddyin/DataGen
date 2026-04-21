namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;

[Cmdlet(VerbsDiagnostic.Test, "SEGenerationPluginPackage")]
[OutputType(typeof(GenerationPluginPackageValidationReport))]
public sealed class TestSEGenerationPluginPackageCommand : PSCmdlet
{
    [Parameter(Mandatory = true)]
    public string[] PluginRootPath { get; set; } = Array.Empty<string>();

    [Parameter(Mandatory = false)]
    public SwitchParameter AllowAssemblyPlugins { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter RequirePluginHashApproval { get; set; }

    [Parameter(Mandatory = false)]
    public string[]? PluginAllowedContentHash { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter ValidatePackContract { get; set; }

    protected override void ProcessRecord()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var validator = services.GetRequiredService<IGenerationPluginPackageValidator>();
        var settings = new ExternalPluginExecutionSettings
        {
            Enabled = true,
            PluginRootPaths = PluginRootPath.ToList(),
            AllowAssemblyPlugins = AllowAssemblyPlugins.IsPresent,
            RequireContentHashAllowList = RequirePluginHashApproval.IsPresent,
            AllowedContentHashes = PluginAllowedContentHash?.ToList() ?? new()
        };

        foreach (var report in validator.Validate(PluginRootPath, settings, ValidatePackContract.IsPresent))
        {
            WriteObject(report);
        }
    }
}
