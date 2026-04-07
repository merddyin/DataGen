namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

[Cmdlet(VerbsCommon.New, "SEEnterpriseWorld")]
[OutputType(typeof(GenerationResult))]
public sealed class NewSEEnterpriseWorldCommand : PSCmdlet
{
    [Parameter(Mandatory = false)]
    public string? CatalogRootPath { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = "Path")]
    public string? ScenarioPath { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = "Json")]
    public string? ScenarioJson { get; set; }

    [Parameter(Mandatory = false)]
    public int? Seed { get; set; }

    protected override void ProcessRecord()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var catalogLoader = services.GetRequiredService<ICatalogLoader>();
        var scenarioLoader = services.GetRequiredService<IScenarioLoader>();
        var worldGenerator = services.GetRequiredService<IWorldGenerator>();

        var catalogs = string.IsNullOrWhiteSpace(CatalogRootPath)
            ? catalogLoader.LoadDefault()
            : catalogLoader.LoadFromPath(CatalogRootPath!);

        var scenario = ParameterSetName == "Json"
            ? scenarioLoader.LoadFromJson(ScenarioJson!)
            : scenarioLoader.LoadFromPath(ScenarioPath!);

        var context = new GenerationContext
        {
            Scenario = scenario,
            Seed = Seed,
            Metadata = new Dictionary<string, string?>
            {
                ["CatalogRootPath"] = CatalogRootPath
            }
        };

        var result = worldGenerator.Generate(context, catalogs);

        WriteObject(result);
    }
}
