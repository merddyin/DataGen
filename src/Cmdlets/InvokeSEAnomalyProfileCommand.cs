using System.Management.Automation;

namespace SyntheticEnterprise.Cmdlets;

[Cmdlet(VerbsLifecycle.Invoke, "SEAnomalyProfile")]
public sealed class InvokeSEAnomalyProfileCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public object InputObject { get; set; } = default!;

    protected override void ProcessRecord()
    {
        // Scaffold only: apply anomaly profiles, normalize the resulting anomaly payloads,
        // and write the updated generation result back to the pipeline.
        WriteObject(InputObject);
    }
}
