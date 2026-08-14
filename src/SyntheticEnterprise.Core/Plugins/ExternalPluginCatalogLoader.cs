namespace SyntheticEnterprise.Core.Plugins;

using System.Security.Cryptography;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Catalogs;

internal static class ExternalPluginCatalogLoader
{
    internal const int MaximumCatalogFileBytes = 16 * 1024 * 1024;
    internal const int MaximumCumulativeCatalogBytes = 64 * 1024 * 1024;
    internal const int MaximumCatalogRows = 100_000;
    internal const int MaximumJsonDepth = 64;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = MaximumJsonDepth
    };

    public static CatalogSet LoadPluginCatalogs(
        GenerationPluginManifest manifest,
        ExternalPluginExecutionSettings settings)
    {
        var csvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);
        var jsonCatalogs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var configuredLimit = Math.Max(1024, settings.MaxInputPayloadBytes);
        var cumulativeLimit = Math.Min(configuredLimit, MaximumCumulativeCatalogBytes);
        var fileLimit = Math.Min(configuredLimit, MaximumCatalogFileBytes);
        var byteBudget = new PluginInputByteBudget(cumulativeLimit);
        var rowBudget = new PluginCatalogRowBudget(MaximumCatalogRows);

        foreach (var path in manifest.LocalDataPaths)
        {
            if (!ExternalPluginPathSecurity.TryValidateNoReparsePoints(path, out var pathWarning))
            {
                throw new PluginPathSecurityException(
                    $"Plugin catalog '{path}' failed plugin path security validation: {pathWarning}");
            }

            using var stream = OpenBoundedCatalog(manifest, path, byteBudget, fileLimit);
            using var authenticatedStream = ReadAndAuthenticateCatalog(manifest, path, stream);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var key = Path.GetFileNameWithoutExtension(path);
            switch (extension)
            {
                case ".csv":
                    csvCatalogs[key] = ReadCsv(authenticatedStream, rowBudget);
                    break;
                case ".json":
                    try
                    {
                        jsonCatalogs[key] = JsonSerializer.Deserialize<object>(authenticatedStream, JsonOptions) ?? new object();
                    }
                    catch (JsonException ex)
                    {
                        throw new PluginPathSecurityException(
                            $"Plugin JSON catalog '{path}' exceeded the maximum depth of {MaximumJsonDepth} or was invalid: {ex.Message}");
                    }
                    break;
                case ".txt":
                    csvCatalogs[key] = ReadText(authenticatedStream, rowBudget);
                    break;
            }
        }

        return new CatalogSet
        {
            CsvCatalogs = csvCatalogs,
            JsonCatalogs = jsonCatalogs
        };
    }

    private static Stream OpenBoundedCatalog(
        GenerationPluginManifest manifest,
        string path,
        PluginInputByteBudget byteBudget,
        long maxFileBytes)
    {
        var stream = ExternalPluginPathSecurity.OpenVerifiedPackageFile(manifest, path, out var warning)
            ?? throw new PluginPathSecurityException(warning!);
        try
        {
            if (stream.Length > maxFileBytes || stream.Length > byteBudget.RemainingBytes)
            {
                throw new PluginInputPayloadLimitExceededException();
            }

            return new BoundedPluginCatalogReadStream(stream, byteBudget, maxFileBytes);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static MemoryStream ReadAndAuthenticateCatalog(
        GenerationPluginManifest manifest,
        string path,
        Stream boundedSource)
    {
        if (!manifest.Provenance.LocalDataHashes.TryGetValue(path, out var expectedHash)
            || string.IsNullOrWhiteSpace(expectedHash))
        {
            throw new PluginPathSecurityException(
                $"Plugin catalog provenance is incomplete for local data path '{path}'.");
        }

        var authenticated = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var bytesRead = boundedSource.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, bytesRead);
                authenticated.Write(buffer, 0, bytesRead);
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginPathSecurityException(
                    $"Plugin catalog hash no longer matches discovered provenance for '{path}'.");
            }

            authenticated.Position = 0;
            return authenticated;
        }
        catch
        {
            authenticated.Dispose();
            throw;
        }
    }

    private static IReadOnlyList<Dictionary<string, string?>> ReadText(
        Stream stream,
        PluginCatalogRowBudget rowBudget)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var rows = new List<Dictionary<string, string?>>();
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                rowBudget.Consume();
                rows.Add(new Dictionary<string, string?> { ["Value"] = line });
            }
        }

        return rows;
    }

    private static IReadOnlyList<Dictionary<string, string?>> ReadCsv(
        Stream stream,
        PluginCatalogRowBudget rowBudget)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var headerLine = reader.ReadLine();
        if (headerLine is null)
        {
            return Array.Empty<Dictionary<string, string?>>();
        }

        var headers = FileSystemCatalogLoader.SplitCsvLine(headerLine);
        var rows = new List<Dictionary<string, string?>>();
        while (reader.ReadLine() is { } line)
        {
            var values = FileSystemCatalogLoader.SplitCsvLine(line);
            if (values.Count == 0 || values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rowBudget.Consume();
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                row[headers[i]] = i < values.Count ? values[i] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private sealed class PluginCatalogRowBudget
    {
        private readonly int _maximumRows;
        private int _rows;

        public PluginCatalogRowBudget(int maximumRows)
        {
            _maximumRows = maximumRows;
        }

        public void Consume()
        {
            if (_rows >= _maximumRows)
            {
                throw new PluginPathSecurityException(
                    $"Plugin text/CSV catalogs exceeded the defensive parsed-row limit of {_maximumRows} rows.");
            }

            _rows++;
        }
    }
}

internal interface IExternalPluginCatalogProvider
{
    CatalogSet Load(GenerationPluginManifest manifest, ExternalPluginExecutionSettings settings);
}

internal sealed class AuthenticatedExternalPluginCatalogProvider : IExternalPluginCatalogProvider
{
    public CatalogSet Load(GenerationPluginManifest manifest, ExternalPluginExecutionSettings settings)
        => ExternalPluginCatalogLoader.LoadPluginCatalogs(manifest, settings);
}
