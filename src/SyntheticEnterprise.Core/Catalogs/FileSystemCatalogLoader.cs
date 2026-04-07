namespace SyntheticEnterprise.Core.Catalogs;

using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Core.Abstractions;

public sealed class FileSystemCatalogLoader : ICatalogLoader
{
    public CatalogSet LoadDefault() => new();

    public CatalogSet LoadFromPath(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Catalog root path is required.", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Catalog path not found: {rootPath}");
        }

        var csvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);
        var jsonCatalogs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(rootPath, "*.csv", SearchOption.AllDirectories))
        {
            csvCatalogs[Path.GetFileNameWithoutExtension(file)] = ReadCsv(file);
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, "*.json", SearchOption.AllDirectories))
        {
            var json = File.ReadAllText(file);
            var document = JsonSerializer.Deserialize<object>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new object();

            jsonCatalogs[Path.GetFileNameWithoutExtension(file)] = document;
        }

        return new CatalogSet
        {
            CsvCatalogs = csvCatalogs,
            JsonCatalogs = jsonCatalogs
        };
    }

    private static IReadOnlyList<Dictionary<string, string?>> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            return Array.Empty<Dictionary<string, string?>>();
        }

        var headers = SplitCsvLine(lines[0]);
        var rows = new List<Dictionary<string, string?>>();

        foreach (var rawLine in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var values = SplitCsvLine(rawLine);
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < headers.Count; i++)
            {
                var value = i < values.Count ? values[i] : null;
                row[headers[i]] = value;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        result.Add(current.ToString());
        return result;
    }
}
