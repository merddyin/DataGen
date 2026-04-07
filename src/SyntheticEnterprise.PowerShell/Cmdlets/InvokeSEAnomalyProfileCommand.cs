namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

[Cmdlet("Invoke", "SEAnomalyProfile")]
[OutputType(typeof(GenerationResult))]
public sealed class InvokeSEAnomalyProfileCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public GenerationResult InputObject { get; set; } = default!;

    [Parameter(Mandatory = false)]
    public SwitchParameter Force { get; set; }

    protected override void ProcessRecord()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var processor = services.GetRequiredService<ILayerProcessor>();
        WriteObject(processor.ApplyAnomalyProfiles(InputObject, new LayerProcessingOptions { ApplyAnomaliesIdempotently = !Force.IsPresent }));
    }
}
