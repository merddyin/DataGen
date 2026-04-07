namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using SyntheticEnterprise.Contracts.Abstractions;

[Cmdlet(VerbsCommon.Get, "SEWorldSummary")]
[OutputType(typeof(GenerationStatistics))]
public sealed class GetSEWorldSummaryCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public GenerationResult InputObject { get; set; } = default!;

    protected override void ProcessRecord()
    {
        WriteObject(InputObject.Statistics);
    }
}
