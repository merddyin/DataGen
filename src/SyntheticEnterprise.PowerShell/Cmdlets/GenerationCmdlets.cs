using System.Management.Automation;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;

namespace SyntheticEnterprise.PowerShell.Cmdlets;

[Cmdlet(VerbsCommon.New, "SEEnterpriseWorld")]
[OutputType(typeof(SyntheticEnterpriseWorld))]
public sealed class NewSEEnterpriseWorldCommand : PSCmdlet
{
    [Parameter(Mandatory = true)]
    public PSObject Catalog { get; set; } = null!;

    [Parameter(Mandatory = true)]
    public ScenarioDefinition Scenario { get; set; } = null!;

    [Parameter]
    public int? RandomSeed { get; set; }

    protected override void ProcessRecord()
    {
        WriteObject(new SyntheticEnterpriseWorld());
    }
}

[Cmdlet(VerbsCommon.Add, "SEIdentityLayer")]
[OutputType(typeof(SyntheticEnterpriseWorld))]
public sealed class AddSEIdentityLayerCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public SyntheticEnterpriseWorld World { get; set; } = null!;

    protected override void ProcessRecord() => WriteObject(World);
}

[Cmdlet(VerbsCommon.Add, "SEInfrastructureLayer")]
[OutputType(typeof(SyntheticEnterpriseWorld))]
public sealed class AddSEInfrastructureLayerCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public SyntheticEnterpriseWorld World { get; set; } = null!;

    protected override void ProcessRecord() => WriteObject(World);
}

[Cmdlet(VerbsCommon.Add, "SERepositoryLayer")]
[OutputType(typeof(SyntheticEnterpriseWorld))]
public sealed class AddSERepositoryLayerCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public SyntheticEnterpriseWorld World { get; set; } = null!;

    protected override void ProcessRecord() => WriteObject(World);
}

[Cmdlet(VerbsLifecycle.Invoke, "SEAnomalyProfile")]
[OutputType(typeof(SyntheticEnterpriseWorld))]
public sealed class InvokeSEAnomalyProfileCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public SyntheticEnterpriseWorld World { get; set; } = null!;

    [Parameter(Mandatory = true)]
    public string Profile { get; set; } = string.Empty;

    protected override void ProcessRecord() => WriteObject(World);
}
