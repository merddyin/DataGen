using System.Collections.Generic;
using SyntheticEnterprise.Exporting.Contracts;

namespace SyntheticEnterprise.Exporting.Services;

public interface IArtifactWriter
{
    ExportArtifactDescriptor Write(
        string outputRoot,
        string relativePath,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        ExportArtifactKind artifactKind);
}
