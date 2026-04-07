using System.Management.Automation;

namespace SyntheticEnterprise.Cmdlets;

[Cmdlet(VerbsCommon.Add, "SEInfrastructureLayer")]
public sealed class AddSEInfrastructureLayerCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public object InputObject { get; set; } = default!;

    [Parameter]
    public string RegenerationMode { get; set; } = "SkipIfPresent";

    protected override void ProcessRecord()
    {
        WriteObject(InputObject);
    }
}
