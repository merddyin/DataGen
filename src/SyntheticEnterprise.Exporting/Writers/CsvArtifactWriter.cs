using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Services;

namespace SyntheticEnterprise.Exporting.Writers;

public sealed class CsvArtifactWriter : IArtifactWriter
{
    public ExportArtifactDescriptor Write(
        string outputRoot,
        string relativePath,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        ExportArtifactKind artifactKind)
    {
        var fullPath = Path.Combine(outputRoot, relativePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? relativePath : $"{relativePath}.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(Escape)));

        foreach (var row in rows)
        {
            var ordered = columns.Select(c => row.TryGetValue(c, out var value) ? Escape(Format(value)) : string.Empty);
            sb.AppendLine(string.Join(",", ordered));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        File.WriteAllBytes(fullPath, bytes);

        return new ExportArtifactDescriptor
        {
            LogicalName = Path.GetFileNameWithoutExtension(fullPath),
            RelativePath = Path.GetRelativePath(outputRoot, fullPath).Replace('\\', '/'),
            ArtifactKind = artifactKind,
            MediaType = "text/csv",
            RowCount = rows.Count,
            Sha256 = ToSha256(bytes),
            SizeBytes = bytes.LongLength,
            Columns = columns
        };
    }

    private static string Format(object? value)
        => value switch
        {
            null => string.Empty,
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    private static string Escape(string value)
    {
        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\"\"");
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value}\""
            : value;
    }

    private static string ToSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
