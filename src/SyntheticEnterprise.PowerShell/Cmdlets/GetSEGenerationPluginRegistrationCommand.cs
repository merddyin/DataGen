namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;

[Cmdlet(VerbsCommon.Get, "SEGenerationPluginRegistration")]
[OutputType(typeof(GenerationPluginRegistration))]
public sealed class GetSEGenerationPluginRegistrationCommand : PSCmdlet
{
    [Parameter(Mandatory = false)]
    public string[]? Capability { get; set; }

    protected override void ProcessRecord()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var registrationService = services.GetRequiredService<IGenerationPluginRegistrationService>();
        var filter = Capability is null || Capability.Length == 0
            ? null
            : new HashSet<string>(Capability, StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrationService.GetAll().Where(item => filter is null || filter.Contains(item.Capability)))
        {
            WriteObject(registration);
        }
    }
}
