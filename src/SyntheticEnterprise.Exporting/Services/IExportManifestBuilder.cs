using System.Collections.Generic;
using SyntheticEnterprise.Exporting.Contracts;

namespace SyntheticEnterprise.Exporting.Services;

public interface IExportManifestBuilder
{
    ExportManifestV2 Build(ExportRequest request, IReadOnlyList<ExportArtifactDescriptor> artifacts);
}
