using System.Management.Automation;
using SyntheticEnterprise.Core.Serialization;
using SyntheticEnterprise.Core.Services;

namespace SyntheticEnterprise.PowerShell.Cmdlets;

[Cmdlet(VerbsData.Import, "SEEnterpriseWorld")]
[OutputType(typeof(object))]
public sealed class ImportSEEnterpriseWorldCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)]
    public string Path { get; set; } = string.Empty;

    [Parameter]
    public SwitchParameter SkipCompatibilityCheck { get; set; }

    [Parameter]
    public SwitchParameter PassThruEnvelope { get; set; }

    protected override void ProcessRecord()
    {
        var serializer = new SnapshotSerializer();
        var compatibility = new SchemaCompatibilityService();
        var persistence = new SnapshotPersistenceService(serializer, compatibility);

        var result = persistence.ImportSnapshot<object>(System.IO.Path.GetFullPath(Path), SkipCompatibilityCheck.IsPresent);

        foreach (var message in result.Compatibility.Messages)
        {
            WriteWarning(message);
        }

        if (PassThruEnvelope.IsPresent)
        {
            WriteObject(result.Envelope);
            return;
        }

        WriteObject(result.Payload);
    }
}
