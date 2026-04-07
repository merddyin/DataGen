using System.Management.Automation;

namespace SyntheticEnterprise.Cmdlets;

[Cmdlet(VerbsLifecycle.Invoke, "SEAnomalyProfile")]
public sealed class InvokeSEAnomalyProfileCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public object InputObject { get; set; } = default!;

    [Parameter]
    public string RegenerationMode { get; set; } = "Merge";

    protected override void ProcessRecord()
    {
        WriteObject(InputObject);
    }
}
