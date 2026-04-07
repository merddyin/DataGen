using SyntheticEnterprise.Exporting.Contracts;

namespace SyntheticEnterprise.Exporting.Services;

public interface IWorldExportCoordinator
{
    ExportManifestV2 Export(object generationResult, ExportRequest request);
}
