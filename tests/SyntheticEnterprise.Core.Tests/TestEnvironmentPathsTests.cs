using System.Reflection;
using System.Security.Cryptography;
using SyntheticEnterprise.Core.Scenarios;

namespace SyntheticEnterprise.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentMutationCollection
{
    public const string Name = "Environment mutation tests";
}

[Collection(EnvironmentMutationCollection.Name)]
public sealed class TestEnvironmentPathsTests
{
    [Fact]
    public void TestAssemblyCarriesCanonicalRepositoryRoot()
    {
        var metadata = typeof(TestEnvironmentPaths).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "DataGenRepositoryRoot");

        Assert.Equal(Path.GetFullPath(metadata.Value!), TestEnvironmentPaths.GetRepositoryRoot());
        Assert.True(File.Exists(Path.Combine(metadata.Value!, "DataGen.slnx")));
    }

    [Fact]
    public void FirstPartyPackResolver_IgnoresRepositoryRootEnvironmentOverride()
    {
        const string variableName = "DATAGEN_REPOSITORY_ROOT";
        var original = Environment.GetEnvironmentVariable(variableName);
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var poisonedRoot = Path.Combine(Path.GetTempPath(), $"datagen-poisoned-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(poisonedRoot, "packs", "first-party"));

        try
        {
            Environment.SetEnvironmentVariable(variableName, poisonedRoot);
            Environment.CurrentDirectory = poisonedRoot;

            var packRoots = new FirstPartyPackPathResolver().ResolvePackRootPaths();

            Assert.DoesNotContain(
                packRoots,
                path => string.Equals(
                    Path.GetFullPath(path),
                    Path.Combine(poisonedRoot, "packs", "first-party"),
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, original);
            Environment.CurrentDirectory = originalCurrentDirectory;
            if (Directory.Exists(poisonedRoot))
            {
                Directory.Delete(poisonedRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void BundledFirstPartyPacks_AreResolvedFromTheIsolatedTestOutput()
    {
        var packRoot = Assert.Single(new FirstPartyPackPathResolver().ResolvePackRootPaths());

        Assert.StartsWith(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "packs", "first-party")),
            Path.GetFullPath(packRoot),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Catalogs_AreResolvedFromTheIsolatedTestOutput()
    {
        var catalogRoot = TestEnvironmentPaths.GetCatalogRoot();

        Assert.StartsWith(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "catalogs")),
            Path.GetFullPath(catalogRoot),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(catalogRoot, "catalogs.sqlite")));
    }

    [Fact]
    public void PluginHostArtifacts_MatchTheFreshIsolatedBuildAndAvoidNormalBin()
    {
        var copiedHostPath = TestEnvironmentPaths.GetPluginHostAssemblyPath();
        var sourceHostPath = File.ReadAllText(TestEnvironmentPaths.GetPluginHostBuildPathManifest()).Trim();
        var isolatedArtifactsRoot = TestEnvironmentPaths.GetPluginHostArtifactsRoot();
        var normalBinRoot = Path.Combine(
            TestEnvironmentPaths.GetRepositoryRoot(),
            "src",
            "SyntheticEnterprise.PluginHost",
            "bin");

        Assert.True(File.Exists(copiedHostPath), $"Copied PluginHost was not found at '{copiedHostPath}'.");
        Assert.True(File.Exists(sourceHostPath), $"Fresh PluginHost was not found at '{sourceHostPath}'.");
        Assert.True(
            IsPathWithin(sourceHostPath, isolatedArtifactsRoot),
            $"PluginHost source '{sourceHostPath}' was not built under '{isolatedArtifactsRoot}'.");
        Assert.StartsWith(AppContext.BaseDirectory, copiedHostPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            IsPathWithin(sourceHostPath, normalBinRoot),
            $"PluginHost source unexpectedly came from product output '{normalBinRoot}'.");

        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceHostPath))),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copiedHostPath))));
    }

    private static bool IsPathWithin(string candidatePath, string directoryPath)
    {
        var relativePath = Path.GetRelativePath(
            Path.GetFullPath(directoryPath),
            Path.GetFullPath(candidatePath));

        return !Path.IsPathRooted(relativePath)
            && !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
