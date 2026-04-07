using System;
using System.IO;
using System.Management.Automation;
using SyntheticEnterprise.Core.Contracts;
using SyntheticEnterprise.Core.Serialization;
using SyntheticEnterprise.Core.Services;

namespace SyntheticEnterprise.PowerShell.Cmdlets;

[Cmdlet(VerbsData.Save, "SEEnterpriseWorld")]
public sealed class SaveSEEnterpriseWorldCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public PSObject InputObject { get; set; } = default!;

    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    [Parameter]
    public string? CatalogRootPath { get; set; }

    [Parameter]
    public string? SourceScenarioPath { get; set; }

    [Parameter]
    public string? SourceScenarioName { get; set; }

    [Parameter]
    public SwitchParameter Compress { get; set; }

    [Parameter]
    public SwitchParameter PassThru { get; set; }

    protected override void ProcessRecord()
    {
        var serializer = new SnapshotSerializer();
        var compatibilityService = new SchemaCompatibilityService();
        var persistence = new SnapshotPersistenceService(serializer, compatibilityService);
        var fingerprintService = new CatalogFingerprintService();

        CatalogContentFingerprint? fingerprint = null;
        if (!string.IsNullOrWhiteSpace(CatalogRootPath) && Directory.Exists(CatalogRootPath))
        {
            fingerprint = fingerprintService.Compute(CatalogRootPath);
        }

        var envelope = persistence.CreateEnvelope(
            payload: InputObject.BaseObject,
            catalogFingerprint: fingerprint,
            sourceScenarioPath: SourceScenarioPath,
            sourceScenarioName: SourceScenarioName,
            warnings: Array.Empty<string>());

        persistence.SaveSnapshot(envelope, ResolveOutputPath(), Compress.IsPresent);

        if (PassThru.IsPresent)
        {
            WriteObject(InputObject);
        }
    }

    private string ResolveOutputPath() => System.IO.Path.GetFullPath(Path);
}
