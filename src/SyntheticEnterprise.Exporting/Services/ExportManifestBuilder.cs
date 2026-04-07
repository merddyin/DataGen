using System;
using System.Collections.Generic;
using SyntheticEnterprise.Exporting.Contracts;

namespace SyntheticEnterprise.Exporting.Services;

public sealed class ExportManifestBuilder : IExportManifestBuilder
{
    public ExportManifestV2 Build(ExportRequest request, IReadOnlyList<ExportArtifactDescriptor> artifacts)
    {
        return new ExportManifestV2
        {
            ExportId = Guid.NewGuid().ToString("N"),
            SchemaVersion = "2.0.0",
            Format = request.Format,
            Profile = request.Profile,
            ExportedAtUtc = request.ExportedAtUtc,
            OutputPath = request.OutputPath,
            Artifacts = artifacts
        };
    }
}
