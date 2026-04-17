namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;

[Cmdlet(VerbsLifecycle.Unregister, "SEGenerationPlugin")]
[OutputType(typeof(int))]
public sealed class UnregisterSEGenerationPluginCommand : PSCmdlet
{
    [Parameter(Mandatory = false)]
    public string[]? Capability { get; set; }

    [Parameter(Mandatory = false)]
    public string[]? PluginRootPath { get; set; }

    protected override void ProcessRecord()
    {
        if ((Capability is null || Capability.Length == 0) && (PluginRootPath is null || PluginRootPath.Length == 0))
        {
            ThrowTerminatingError(new ErrorRecord(
                new PSArgumentException("Specify Capability or PluginRootPath when unregistering plugins."),
                "MissingUnregisterFilter",
                ErrorCategory.InvalidArgument,
                targetObject: null));
        }

        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var registrationService = services.GetRequiredService<IGenerationPluginRegistrationService>();
        var removed = registrationService.Unregister(Capability, PluginRootPath);
        WriteObject(removed);
    }
}
