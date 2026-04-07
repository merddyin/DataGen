using System.Management.Automation;
using SyntheticEnterprise.Services;

namespace SyntheticEnterprise.Cmdlets;

[Cmdlet(VerbsCommon.Add, "SEIdentityLayer")]
public sealed class AddSEIdentityLayerCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public object InputObject { get; set; } = default!;

    [Parameter]
    public string RegenerationMode { get; set; } = "SkipIfPresent";

    protected override void ProcessRecord()
    {
        // Integration note:
        // wire to the real GenerationResult model, layer processor, session manifest service,
        // and reference repair pipeline from the actual repository.
        WriteObject(InputObject);
    }
}
