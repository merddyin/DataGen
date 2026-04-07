using SyntheticEnterprise.Exporting.Contracts;

namespace SyntheticEnterprise.Exporting.Services;

public interface IExportSummaryBuilder
{
    ExportSummary Build(object generationResult, int artifactCount);
}
