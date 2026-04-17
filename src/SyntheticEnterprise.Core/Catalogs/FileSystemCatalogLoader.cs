namespace SyntheticEnterprise.Core.Catalogs;

using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Core.Abstractions;

public sealed class FileSystemCatalogLoader : ICatalogLoader
{
    internal const string ManifestFileName = "catalog-import-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogSet LoadDefault()
    {
        foreach (var candidate in EnumerateDefaultRootCandidates())
        {
            var resolved = TryResolveCatalogDirectory(candidate);
            if (resolved is not null)
            {
                return LoadFromPath(resolved);
            }
        }

        return new CatalogSet();
    }

    public CatalogSet LoadFromPath(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Catalog root path is required.", nameof(rootPath));
        }

        var fullPath = Path.GetFullPath(rootPath);
        if (File.Exists(fullPath) && IsSqlitePath(fullPath))
        {
            return LoadFromSqlite(fullPath);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Catalog path not found: {rootPath}");
        }

        var defaultDbPath = Path.Combine(fullPath, "catalogs.sqlite");
        if (File.Exists(defaultDbPath))
        {
            return LoadFromSqlite(defaultDbPath);
        }

        return LoadFromDirectory(fullPath);
    }

    private static CatalogSet LoadFromDirectory(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return new CatalogSet();
        }

        var csvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);
        var jsonCatalogs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<CatalogSourceMetadata>();

        foreach (var file in Directory.EnumerateFiles(rootPath, "*.csv", SearchOption.AllDirectories))
        {
            csvCatalogs[Path.GetFileNameWithoutExtension(file)] = ReadCsv(file);
            sources.Add(new CatalogSourceMetadata
            {
                CatalogName = Path.GetFileNameWithoutExtension(file),
                SourceFile = Path.GetRelativePath(rootPath, file),
                SourceRoot = rootPath,
                SourceKind = "csv",
                Strategy = "filesystem"
            });
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, "*.json", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var json = ReadAllTextShared(file);
            var document = JsonSerializer.Deserialize<object>(json, JsonOptions) ?? new object();

            jsonCatalogs[Path.GetFileNameWithoutExtension(file)] = document;
            sources.Add(new CatalogSourceMetadata
            {
                CatalogName = Path.GetFileNameWithoutExtension(file),
                SourceFile = Path.GetRelativePath(rootPath, file),
                SourceRoot = rootPath,
                SourceKind = "json",
                Strategy = "filesystem"
            });
        }

        string? manifestVersion = null;
        var manifestPath = Path.Combine(rootPath, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            manifestVersion = JsonSerializer.Deserialize<CatalogImportManifest>(ReadAllTextShared(manifestPath), JsonOptions)?.Version;
        }

        return new CatalogSet
        {
            CsvCatalogs = csvCatalogs,
            JsonCatalogs = jsonCatalogs,
            BuildMetadata = new CatalogBuildMetadata
            {
                Version = "filesystem",
                ManifestVersion = manifestVersion
            },
            Sources = sources
        };
    }

    private static CatalogSet LoadFromSqlite(string databasePath)
    {
        EnsureSqliteInitialized();

        var csvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);
        var jsonCatalogs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<CatalogSourceMetadata>();
        CatalogBuildMetadata? buildMetadata = null;

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND substr(name, 1, 2) <> '__'";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var tableName = reader.GetString(0);
                if (string.Equals(tableName, "json_catalogs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                csvCatalogs[tableName] = ReadSqliteTable(connection, tableName);
            }
        }

        using (var jsonCommand = connection.CreateCommand())
        {
            jsonCommand.CommandText = "SELECT catalog_name, json_payload FROM json_catalogs";
            using var reader = jsonCommand.ExecuteReader();
            while (reader.Read())
            {
                var catalogName = reader.GetString(0);
                var payload = reader.GetString(1);
                jsonCatalogs[catalogName] = JsonSerializer.Deserialize<object>(payload, JsonOptions) ?? new object();
            }
        }

        if (TableExists(connection, "__catalog_build"))
        {
            using var buildCommand = connection.CreateCommand();
            buildCommand.CommandText = "SELECT built_at_utc, version, manifest_version FROM \"__catalog_build\" LIMIT 1";
            using var reader = buildCommand.ExecuteReader();
            if (reader.Read())
            {
                buildMetadata = new CatalogBuildMetadata
                {
                    BuiltAtUtc = reader.IsDBNull(0) ? null : reader.GetString(0),
                    Version = reader.IsDBNull(1) ? null : reader.GetString(1),
                    ManifestVersion = reader.IsDBNull(2) ? null : reader.GetString(2)
                };
            }
        }

        if (TableExists(connection, "__catalog_source"))
        {
            using var sourceCommand = connection.CreateCommand();
            sourceCommand.CommandText = "SELECT catalog_name, source_file, source_root, source_kind, strategy FROM \"__catalog_source\"";
            using var reader = sourceCommand.ExecuteReader();
            while (reader.Read())
            {
                sources.Add(new CatalogSourceMetadata
                {
                    CatalogName = reader.GetString(0),
                    SourceFile = reader.GetString(1),
                    SourceRoot = reader.GetString(2),
                    SourceKind = reader.GetString(3),
                    Strategy = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
        }

        return new CatalogSet
        {
            CsvCatalogs = csvCatalogs,
            JsonCatalogs = jsonCatalogs,
            BuildMetadata = buildMetadata,
            Sources = sources
        };
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static IReadOnlyList<Dictionary<string, string?>> ReadSqliteTable(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{tableName}\"";
        using var reader = command.ExecuteReader();

        var rows = new List<Dictionary<string, string?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();
            }

            rows.Add(row);
        }

        return rows;
    }

    private static IReadOnlyList<Dictionary<string, string?>> ReadCsv(string path)
    {
        using var enumerator = EnumerateLinesShared(path).GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return Array.Empty<Dictionary<string, string?>>();
        }

        var headers = SplitCsvLine(enumerator.Current);
        var rows = new List<Dictionary<string, string?>>();

        while (enumerator.MoveNext())
        {
            var rawLine = enumerator.Current;
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

    internal static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
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

    private static bool IsSqlitePath(string path)
        => path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".db", StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> EnumerateDefaultRootCandidates()
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void AddCandidate(List<string> items, HashSet<string> seenPaths, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var normalized = Path.GetFullPath(path);
                if (seenPaths.Add(normalized))
                {
                    items.Add(normalized);
                }
            }
            catch (Exception)
            {
            }
        }

        AddCandidate(candidates, seen, Environment.CurrentDirectory);
        AddCandidate(candidates, seen, AppContext.BaseDirectory);
        AddCandidate(candidates, seen, Path.GetDirectoryName(typeof(FileSystemCatalogLoader).Assembly.Location));
        AddCandidate(candidates, seen, Path.GetDirectoryName(typeof(ICatalogLoader).Assembly.Location));
        AddCandidate(candidates, seen, Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location));

        return candidates;
    }

    internal static string? TryResolveCatalogDirectory(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (string.Equals(current.Name, "catalogs", StringComparison.OrdinalIgnoreCase) && current.Exists)
            {
                return current.FullName;
            }

            var catalogDirectory = Path.Combine(current.FullName, "catalogs");
            if (Directory.Exists(catalogDirectory))
            {
                return catalogDirectory;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void EnsureSqliteInitialized()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    internal static IEnumerable<string> EnumerateLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            yield return reader.ReadLine() ?? string.Empty;
        }
    }

    internal static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
