using System.Globalization;
using System.Security.Cryptography;

namespace SyntheticEnterprise.Tests;

internal static class IntegrationTestAssets
{
    private const int ExpectedAssetCount = 76;
    private const string ManifestFileName = "integration-test-assets.manifest";

    private static readonly string[] MirrorRoots =
    {
        "catalogs",
        "examples",
        Path.Combine("packs", "first-party")
    };

    public static string GetOutputPath(params string[] segments)
        => CombinePath(AppContext.BaseDirectory, segments);

    public static void AssertMirrorIsValid()
    {
        var entries = ReadManifest();
        Assert.Equal(ExpectedAssetCount, entries.Count);

        var manifestPaths = entries.Select(entry => entry.RelativePath).ToArray();
        Assert.Equal(
            manifestPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            manifestPaths);
        Assert.Equal(
            manifestPaths.Length,
            manifestPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var outputPaths = MirrorRoots
            .Select(root => GetOutputPath(root))
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(AppContext.BaseDirectory, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(manifestPaths, outputPaths);
        foreach (var entry in entries)
        {
            AssertEntryMatchesOutput(entry);
        }
    }

    public static void AssertMirroredFile(params string[] segments)
    {
        var relativePath = NormalizeRelativePath(Path.Combine(segments));
        var entry = Assert.Single(
            ReadManifest(),
            candidate => candidate.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

        AssertEntryMatchesOutput(entry);
    }

    public static bool IsPathWithin(string candidatePath, string directoryPath)
    {
        var relativePath = Path.GetRelativePath(
            Path.GetFullPath(directoryPath),
            Path.GetFullPath(candidatePath));

        return !Path.IsPathRooted(relativePath)
            && !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static IReadOnlyList<ManifestEntry> ReadManifest()
    {
        var manifestPath = GetOutputPath(ManifestFileName);
        Assert.True(File.Exists(manifestPath), $"Integration asset manifest was not found at '{manifestPath}'.");

        var entries = new List<ManifestEntry>();
        foreach (var line in File.ReadLines(manifestPath).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var fields = line.Split('|');
            Assert.Equal(3, fields.Length);

            var relativePath = NormalizeRelativePath(fields[0]);
            Assert.Equal(fields[0], relativePath);
            Assert.True(
                long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var size),
                $"Manifest size was invalid for '{relativePath}'.");
            Assert.Equal(64, fields[2].Length);
            Assert.All(fields[2], character => Assert.True(Uri.IsHexDigit(character)));

            var outputPath = GetOutputPath(relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(
                IsPathWithin(outputPath, AppContext.BaseDirectory),
                $"Manifest path escaped the test output: '{relativePath}'.");

            entries.Add(new ManifestEntry(relativePath, size, fields[2]));
        }

        return entries;
    }

    private static void AssertEntryMatchesOutput(ManifestEntry entry)
    {
        var outputPath = GetOutputPath(entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(outputPath), $"Mirrored test asset was not found at '{outputPath}'.");
        Assert.Equal(entry.Size, new FileInfo(outputPath).Length);
        Assert.Equal(entry.Sha256, ComputeSha256(outputPath), ignoreCase: true);
    }

    private static string CombinePath(string root, IEnumerable<string> segments)
    {
        var path = root;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return Path.GetFullPath(path);
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string NormalizeRelativePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private sealed record ManifestEntry(string RelativePath, long Size, string Sha256);
}
