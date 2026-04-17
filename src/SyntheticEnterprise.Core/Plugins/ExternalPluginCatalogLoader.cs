namespace SyntheticEnterprise.Core.Plugins;

using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Catalogs;

internal static class ExternalPluginCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CatalogSet LoadPluginCatalogs(GenerationPluginManifest manifest)
    {
        var csvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);
        var jsonCatalogs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in manifest.LocalDataPaths)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var key = Path.GetFileNameWithoutExtension(path);
            switch (extension)
            {
                case ".csv":
                    csvCatalogs[key] = ReadCsv(path);
                    break;
                case ".json":
                    jsonCatalogs[key] = JsonSerializer.Deserialize<object>(File.ReadAllText(path), JsonOptions) ?? new object();
                    break;
                case ".txt":
                    csvCatalogs[key] = File.ReadAllLines(path)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Select(line => new Dictionary<string, string?> { ["Value"] = line })
                        .Cast<Dictionary<string, string?>>()
                        .ToList();
                    break;
            }
        }

        return new CatalogSet
        {
            CsvCatalogs = csvCatalogs,
            JsonCatalogs = jsonCatalogs
        };
    }

    private static IReadOnlyList<Dictionary<string, string?>> ReadCsv(string path)
    {
        using var enumerator = FileSystemCatalogLoader.EnumerateLinesShared(path).GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return Array.Empty<Dictionary<string, string?>>();
        }

        var headers = FileSystemCatalogLoader.SplitCsvLine(enumerator.Current);
        var rows = new List<Dictionary<string, string?>>();
        while (enumerator.MoveNext())
        {
            var values = FileSystemCatalogLoader.SplitCsvLine(enumerator.Current);
            if (values.Count == 0 || values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                row[headers[i]] = i < values.Count ? values[i] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }
}
