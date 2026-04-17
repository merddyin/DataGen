using System.Text;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Core.Catalogs;

return await CatalogToolProgram.RunAsync(args);

internal static class CatalogToolProgram
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return Task.FromResult(1);
        }

        var command = args[0].Trim().ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());

        return command switch
        {
            "build" => Task.FromResult(Build(options)),
            "compare" => Task.FromResult(Compare(options)),
            "export" => Task.FromResult(Export(options)),
            _ => Task.FromResult(UnknownCommand(command))
        };
    }

    private static int Build(IReadOnlyDictionary<string, string?> options)
    {
        var catalogRoot = RequireOption(options, "catalog-root");
        var outputPath = GetOption(options, "output") ?? Path.Combine(catalogRoot, "catalogs.sqlite");
        var includeRawNamesCache = HasSwitch(options, "include-raw-names-cache");
        var includeUncuratedSources = HasSwitch(options, "include-uncurated-sources");

        var sourceRoots = new List<string> { Path.GetFullPath(catalogRoot) };

        var originRoot = GetOption(options, "origin-root");
        if (!string.IsNullOrWhiteSpace(originRoot) && Directory.Exists(originRoot))
        {
            sourceRoots.Add(Path.GetFullPath(originRoot));
        }

        var rawNamesRoot = GetOption(options, "raw-names-root");
        if (includeRawNamesCache && !string.IsNullOrWhiteSpace(rawNamesRoot) && Directory.Exists(rawNamesRoot))
        {
            sourceRoots.Add(Path.GetFullPath(rawNamesRoot));
        }

        CatalogSqliteDatabaseBuilder.Build(Path.GetFullPath(outputPath), sourceRoots, includeUncuratedSources);
        Console.WriteLine(Path.GetFullPath(outputPath));
        return 0;
    }

    private static int Compare(IReadOnlyDictionary<string, string?> options)
    {
        var leftPath = RequireOption(options, "left");
        var rightPath = RequireOption(options, "right");

        var loader = new FileSystemCatalogLoader();
        var left = loader.LoadFromPath(leftPath);
        var right = loader.LoadFromPath(rightPath);

        var differences = new List<string>();

        if (!string.Equals(left.BuildMetadata?.Version, right.BuildMetadata?.Version, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Build metadata version differs: '{left.BuildMetadata?.Version}' vs '{right.BuildMetadata?.Version}'.");
        }

        if (!string.Equals(left.BuildMetadata?.ManifestVersion, right.BuildMetadata?.ManifestVersion, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Manifest version differs: '{left.BuildMetadata?.ManifestVersion}' vs '{right.BuildMetadata?.ManifestVersion}'.");
        }

        CompareCatalogMaps("CSV catalog", left.CsvCatalogs, right.CsvCatalogs, NormalizeRows, differences);
        CompareCatalogMaps("JSON catalog", left.JsonCatalogs, right.JsonCatalogs, NormalizeJsonCatalog, differences);

        var leftSources = left.Sources.Select(NormalizeSource).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var rightSources = right.Sources.Select(NormalizeSource).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        CompareNormalizedCollections("Catalog sources", leftSources, rightSources, differences);

        if (differences.Count == 0)
        {
            Console.WriteLine("Catalogs are logically equivalent.");
            return 0;
        }

        Console.Error.WriteLine("Catalogs differ:");
        foreach (var difference in differences)
        {
            Console.Error.WriteLine($"- {difference}");
        }

        return 1;
    }

    private static int Export(IReadOnlyDictionary<string, string?> options)
    {
        var inputPath = RequireOption(options, "input");
        var tableName = RequireOption(options, "table");
        var outputPath = RequireOption(options, "output");

        var loader = new FileSystemCatalogLoader();
        var catalogs = loader.LoadFromPath(inputPath);

        if (!catalogs.CsvCatalogs.TryGetValue(tableName, out var rows))
        {
            throw new ArgumentException($"CSV catalog '{tableName}' was not found in '{inputPath}'.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        WriteCsv(outputPath, rows);
        Console.WriteLine(Path.GetFullPath(outputPath));
        return 0;
    }

    private static void CompareCatalogMaps<T>(
        string label,
        IReadOnlyDictionary<string, T> left,
        IReadOnlyDictionary<string, T> right,
        Func<T, IReadOnlyList<string>> normalizer,
        ICollection<string> differences)
    {
        var leftKeys = left.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var rightKeys = right.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        CompareNormalizedCollections($"{label} keys", leftKeys, rightKeys, differences);

        foreach (var key in leftKeys.Intersect(rightKeys, StringComparer.OrdinalIgnoreCase))
        {
            var leftRows = normalizer(left[key]);
            var rightRows = normalizer(right[key]);
            CompareNormalizedCollections($"{label} '{key}'", leftRows, rightRows, differences);
        }
    }

    private static IReadOnlyList<string> NormalizeRows(IReadOnlyList<Dictionary<string, string?>> rows)
        => rows
            .Select(row => string.Join("|",
                row.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => $"{Escape(pair.Key)}={Escape(pair.Value)}")))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> NormalizeJsonCatalog(object catalog)
        => new[] { CanonicalizeJson(JsonSerializer.Serialize(catalog)) };

    private static string NormalizeSource(CatalogSourceMetadata source)
        => string.Join("|", new[]
        {
            Escape(source.CatalogName),
            Escape(source.SourceFile),
            Escape(source.SourceRoot),
            Escape(source.SourceKind),
            Escape(source.Strategy)
        });

    private static void CompareNormalizedCollections(
        string label,
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        ICollection<string> differences)
    {
        if (left.Count != right.Count)
        {
            differences.Add($"{label} count differs: {left.Count} vs {right.Count}.");
            return;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                continue;
            }

            differences.Add($"{label} differs at position {i + 1}.");
            return;
        }
    }

    private static string CanonicalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return CanonicalizeElement(document.RootElement);
    }

    private static string CanonicalizeElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{JsonSerializer.Serialize(property.Name)}:{CanonicalizeElement(property.Value)}")) + "}",
            JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(CanonicalizeElement)) + "]",
            _ => element.GetRawText()
        };
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{token}'.");
            }

            var key = token[2..];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = null;
                continue;
            }

            options[key] = args[++i];
        }

        return options;
    }

    private static string RequireOption(IReadOnlyDictionary<string, string?> options, string key)
    {
        if (options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new ArgumentException($"Missing required option '--{key}'.");
    }

    private static string? GetOption(IReadOnlyDictionary<string, string?> options, string key)
        => options.TryGetValue(key, out var value) ? value : null;

    private static bool HasSwitch(IReadOnlyDictionary<string, string?> options, string key)
        => options.ContainsKey(key);

    private static string Escape(string? value)
        => value?.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal) ?? "<null>";

    private static void WriteCsv(string outputPath, IReadOnlyList<Dictionary<string, string?>> rows)
    {
        using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (rows.Count == 0)
        {
            return;
        }

        var headers = rows[0].Keys.ToArray();
        writer.WriteLine(string.Join(",", headers.Select(EscapeCsv)));

        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(",", headers.Select(header => EscapeCsv(row.TryGetValue(header, out var value) ? value : null))));
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        }

        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? $"\"{value}\""
            : value;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("SyntheticEnterprise.CatalogTool");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  build   --catalog-root <path> [--output <path>] [--origin-root <path>] [--raw-names-root <path>] [--include-raw-names-cache] [--include-uncurated-sources]");
        Console.WriteLine("  compare --left <path> --right <path>");
        Console.WriteLine("  export  --input <path> --table <name> --output <path>");
    }
}
