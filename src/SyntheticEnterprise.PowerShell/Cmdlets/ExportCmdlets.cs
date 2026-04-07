using System.Management.Automation;
using SyntheticEnterprise.Contracts.Models;

namespace SyntheticEnterprise.PowerShell.Cmdlets;

[Cmdlet(VerbsData.Export, "SEWorld")]
public sealed class ExportSEWorldCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public SyntheticEnterpriseWorld World { get; set; } = null!;

    [Parameter(Mandatory = true)]
    public string OutputPath { get; set; } = string.Empty;

    [Parameter]
    [ValidateSet("Csv", "Json")]
    public string Format { get; set; } = "Csv";

    [Parameter]
    public SwitchParameter IncludeManifest { get; set; }

    protected override void ProcessRecord()
    {
        // Placeholder: call export service from SyntheticEnterprise.Core
        WriteObject(OutputPath);
    }
}

[Cmdlet(VerbsCommon.Get, "SEWorldSummary")]
[OutputType(typeof(object))]
public sealed class GetSEWorldSummaryCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public SyntheticEnterpriseWorld World { get; set; } = null!;

    protected override void ProcessRecord()
    {
        WriteObject(new
        {
            Companies = World.Companies.Count,
            People = World.People.Count,
            Accounts = World.Accounts.Count,
            Devices = World.Devices.Count,
            Repositories = World.Repositories.Count
        });
    }
}
