namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;

[Cmdlet(VerbsCommon.New, "SEGenerationPluginPackage")]
[OutputType(typeof(GenerationPluginPackageScaffoldResult))]
public sealed class NewSEGenerationPluginPackageCommand : PSCmdlet
{
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    [Parameter(Mandatory = true)]
    public string Capability { get; set; } = string.Empty;

    [Parameter(Mandatory = false)]
    public string? DisplayName { get; set; }

    [Parameter(Mandatory = false)]
    public string? Description { get; set; }

    [Parameter(Mandatory = false)]
    public string PluginKind { get; set; } = "ScenarioPack";

    [Parameter(Mandatory = false)]
    public string PackPhase { get; set; } = "PostWorldGeneration";

    [Parameter(Mandatory = false)]
    public string Category { get; set; } = "Custom";

    [Parameter(Mandatory = false)]
    public SwitchParameter Force { get; set; }

    protected override void ProcessRecord()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var scaffolder = services.GetRequiredService<IGenerationPluginPackageScaffolder>();
        var result = scaffolder.Scaffold(new GenerationPluginPackageScaffoldRequest
        {
            RootPath = Path,
            Capability = Capability,
            DisplayName = DisplayName,
            Description = Description,
            PluginKind = PluginKind,
            PackPhase = PackPhase,
            Category = Category,
            Force = Force.IsPresent
        });

        WriteObject(result);
    }
}
