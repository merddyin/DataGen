namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;

[Cmdlet(VerbsLifecycle.Register, "SEGenerationPlugin")]
[OutputType(typeof(GenerationPluginRegistrationResult))]
public sealed class RegisterSEGenerationPluginCommand : PSCmdlet
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

        var registrationService = services.GetRequiredService<IGenerationPluginRegistrationService>();
        var result = registrationService.Register(PluginRootPath, AllowAssemblyPlugins.IsPresent);
        WriteObject(result);
    }
}
