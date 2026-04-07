namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

[Cmdlet(VerbsCommon.Add, "SERepositoryLayer")]
[OutputType(typeof(GenerationResult))]
public sealed class AddSERepositoryLayerCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public GenerationResult InputObject { get; set; } = default!;

    [Parameter(Mandatory = false)]
    public LayerRegenerationMode RegenerationMode { get; set; } = LayerRegenerationMode.SkipIfPresent;

    protected override void ProcessRecord()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var processor = services.GetRequiredService<ILayerProcessor>();
        WriteObject(processor.AddRepositoryLayer(InputObject, new LayerProcessingOptions { RepositoryMode = RegenerationMode }));
    }
}
