using SyntheticEnterprise.Exporting.Contracts;

namespace SyntheticEnterprise.Exporting.Services;

public sealed class ExportSummaryBuilder : IExportSummaryBuilder
{
    public ExportSummary Build(object generationResult, int artifactCount)
    {
        return new ExportSummary
        {
            ArtifactCount = artifactCount
        };
    }
}
