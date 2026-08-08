using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SyntheticEnterprise.Exporting.Contracts;

namespace SyntheticEnterprise.Exporting.Services;

public sealed class ExportManifestBuilder : IExportManifestBuilder
{
    public ExportManifestV2 Build(ExportRequest request, IReadOnlyList<ExportArtifactDescriptor> artifacts)
    {
        return new ExportManifestV2
        {
            ExportId = CreateDeterministicExportId(request, artifacts),
            SchemaVersion = "2.0.0",
            Format = request.Format,
            Profile = request.Profile,
            ExportedAtUtc = request.ExportedAtUtc,
            OutputPath = request.OutputPath,
            Artifacts = artifacts
        };
    }

    private static string CreateDeterministicExportId(ExportRequest request, IReadOnlyList<ExportArtifactDescriptor> artifacts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "SyntheticEnterprise.ExportManifestV2.ExportId/v1");
        AppendString(hash, "2.0.0");
        AppendString(hash, request.Format.ToString());
        AppendString(hash, request.Profile.ToString());
        AppendString(hash, request.ExportedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        var orderedArtifacts = artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.LogicalName, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.Sha256, StringComparer.Ordinal)
            .ToArray();
        AppendInt32(hash, orderedArtifacts.Length);

        foreach (var artifact in orderedArtifacts)
        {
            AppendString(hash, artifact.LogicalName);
            AppendString(hash, artifact.RelativePath);
            AppendString(hash, artifact.ArtifactKind.ToString());
            AppendString(hash, artifact.MediaType);
            AppendInt64(hash, artifact.RowCount);
            AppendString(hash, artifact.Sha256);
            AppendInt64(hash, artifact.SizeBytes);
            AppendInt32(hash, artifact.Columns.Count);
            foreach (var column in artifact.Columns)
            {
                AppendString(hash, column);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()[..16]).ToLowerInvariant();
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
