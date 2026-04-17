using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SyntheticEnterprise.Core.Contracts;

namespace SyntheticEnterprise.Core.Services;

public sealed class CatalogFingerprintService : ICatalogFingerprintService
{
    public CatalogContentFingerprint Compute(string catalogRootPath)
    {
        var root = Path.GetFullPath(catalogRootPath);
        var files = Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();

        var result = new CatalogContentFingerprint
        {
            RootPath = root
        };

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            result.Files.Add(new CatalogFileFingerprint
            {
                RelativePath = Path.GetRelativePath(root, file),
                FileSizeBytes = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
                Sha256 = ComputeSha256(file),
                RowCount = CountRows(file),
                SchemaSignature = ComputeSchemaSignature(file)
            });
        }

        var aggregateMaterial = string.Join("\n", result.Files.Select(f => $"{f.RelativePath}|{f.Sha256}|{f.RowCount}|{f.SchemaSignature}"));
        result.AggregateSha256 = ComputeSha256FromString(aggregateMaterial);
        return result;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string ComputeSha256FromString(string value)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static int CountRows(string path)
    {
        try
        {
            return File.ReadLines(path).Skip(1).Count();
        }
        catch
        {
            return 0;
        }
    }

    private static string ComputeSchemaSignature(string path)
    {
        try
        {
            var header = File.ReadLines(path).FirstOrDefault() ?? string.Empty;
            return ComputeSha256FromString(header.Trim());
        }
        catch
        {
            return string.Empty;
        }
    }
}
