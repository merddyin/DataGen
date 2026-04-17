using System.Management.Automation;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Services;

namespace SyntheticEnterprise.PowerShell.Cmdlets;

[Cmdlet(VerbsData.Export, "SEEnterpriseWorld")]
[OutputType(typeof(object))]
public sealed class ExportSEEnterpriseWorldCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public object InputObject { get; set; } = default!;

    [Parameter(Mandatory = true)]
    public string OutputPath { get; set; } = string.Empty;

    [Parameter]
    public ExportSerializationFormat Format { get; set; } = ExportSerializationFormat.Csv;

    [Parameter]
    public ExportProfileKind Profile { get; set; } = ExportProfileKind.Normalized;

    [Parameter]
    public string? ArtifactPrefix { get; set; }

    [Parameter]
    public SwitchParameter IncludeManifest { get; set; } = true;

    [Parameter]
    public SwitchParameter IncludeSummary { get; set; } = true;

    [Parameter]
    public SwitchParameter Overwrite { get; set; }

    [Parameter]
    public CredentialExportMode CredentialExportMode { get; set; } = CredentialExportMode.Masked;

    [Parameter]
    public SwitchParameter PassThru { get; set; }

    private IWorldExportCoordinator? _coordinator;

    protected override void BeginProcessing()
    {
        // Resolve from module DI during integration.
        _coordinator ??= new WorldExportCoordinator(
            new SyntheticEnterprise.Exporting.Profiles.NormalizedEntityTableProvider(),
            new SyntheticEnterprise.Exporting.Profiles.NormalizedLinkTableProvider(),
            Format == ExportSerializationFormat.Csv
                ? new SyntheticEnterprise.Exporting.Writers.CsvArtifactWriter()
                : new SyntheticEnterprise.Exporting.Writers.JsonArtifactWriter(),
            new ExportManifestBuilder(),
            new ExportSummaryBuilder(),
            new ExportPathResolver());
    }

    protected override void ProcessRecord()
    {
        var exportInput = InputObject is PSObject psObject
            ? psObject.BaseObject ?? InputObject
            : InputObject;

        var request = new ExportRequest
        {
            Format = Format,
            Profile = Profile,
            OutputPath = OutputPath,
            ArtifactPrefix = ArtifactPrefix,
            IncludeManifest = IncludeManifest.IsPresent,
            IncludeSummary = IncludeSummary.IsPresent,
            Overwrite = Overwrite.IsPresent,
            CredentialExportMode = CredentialExportMode
        };

        var manifest = _coordinator!.Export(exportInput, request);

        WriteVerbose($"Exported {manifest.Artifacts.Count} artifacts to '{manifest.OutputPath}'.");

        if (PassThru.IsPresent)
        {
            WriteObject(InputObject);
        }
        else
        {
            WriteObject(manifest);
        }
    }
}
