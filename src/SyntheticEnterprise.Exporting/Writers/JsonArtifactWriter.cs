using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Services;

namespace SyntheticEnterprise.Exporting.Writers;

public sealed class JsonArtifactWriter : IArtifactWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public ExportArtifactDescriptor Write(
        string outputRoot,
        string relativePath,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        ExportArtifactKind artifactKind)
    {
        var fullPath = Path.Combine(outputRoot, relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? relativePath : $"{relativePath}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(rows, SerializerOptions);
        File.WriteAllBytes(fullPath, bytes);

        return new ExportArtifactDescriptor
        {
            LogicalName = Path.GetFileNameWithoutExtension(fullPath),
            RelativePath = Path.GetRelativePath(outputRoot, fullPath).Replace('\\', '/'),
            ArtifactKind = artifactKind,
            MediaType = "application/json",
            RowCount = rows.Count,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            SizeBytes = bytes.LongLength,
            Columns = columns
        };
    }
}
