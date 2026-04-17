namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;

[Cmdlet(VerbsCommon.Get, "SEGenerationPlugin")]
[OutputType(typeof(GenerationPluginInspectionRecord))]
public sealed class GetSEGenerationPluginCommand : PSCmdlet
{
    [Parameter(Mandatory = true)]
    public string[] PluginRootPath { get; set; } = Array.Empty<string>();

    [Parameter(Mandatory = false)]
    public string[]? Capability { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter AllowAssemblyPlugins { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter RequirePluginHashApproval { get; set; }

    [Parameter(Mandatory = false)]
    public string[]? PluginAllowedContentHash { get; set; }

    protected override void ProcessRecord()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var catalog = services.GetRequiredService<IExternalGenerationPluginCatalog>();
        var settings = new ExternalPluginExecutionSettings
        {
            Enabled = true,
            PluginRootPaths = PluginRootPath.ToList(),
            AllowAssemblyPlugins = AllowAssemblyPlugins.IsPresent,
            RequireContentHashAllowList = RequirePluginHashApproval.IsPresent,
            AllowedContentHashes = PluginAllowedContentHash?.ToList() ?? new()
        };
        var capabilityFilter = Capability is null || Capability.Length == 0
            ? null
            : new HashSet<string>(Capability, StringComparer.OrdinalIgnoreCase);

        var records = catalog
            .Inspect(PluginRootPath, settings)
            .Where(record => capabilityFilter is null || capabilityFilter.Contains(record.Capability));

        foreach (var record in records)
        {
            WriteObject(record);
        }
    }
}
