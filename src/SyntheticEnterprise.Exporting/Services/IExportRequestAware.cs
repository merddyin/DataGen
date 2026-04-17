using SyntheticEnterprise.Exporting.Contracts;

namespace SyntheticEnterprise.Exporting.Services;

public interface IExportRequestAware
{
    void ApplyRequest(ExportRequest request);
}
