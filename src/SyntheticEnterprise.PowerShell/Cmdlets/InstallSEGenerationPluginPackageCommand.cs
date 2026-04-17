namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;

[Cmdlet("Install", "SEGenerationPluginPackage")]
[OutputType(typeof(GenerationPluginInstallationResult))]
public sealed class InstallSEGenerationPluginPackageCommand : PSCmdlet
{
    [Parameter(Mandatory = true)]
    public string[] PluginRootPath { get; set; } = Array.Empty<string>();

    [Parameter(Mandatory = false)]
    public SwitchParameter AllowAssemblyPlugins { get; set; }

    protected override void ProcessRecord()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var installer = services.GetRequiredService<IGenerationPluginInstallationService>();
        var result = installer.Install(PluginRootPath, AllowAssemblyPlugins.IsPresent);
        WriteObject(result);
    }
}
