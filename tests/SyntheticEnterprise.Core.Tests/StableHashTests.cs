using System.Runtime.CompilerServices;
using SyntheticEnterprise.Core.Generation;
using Xunit;

namespace SyntheticEnterprise.Core.Tests;

public sealed class StableHashTests
{
    [Fact]
    public void GetIndex_IsStableForEquivalentComponents()
    {
        var first = StableHash.GetIndex("tests.stability", 9_000_000, "COMP-000001", "manufacturing");
        var second = StableHash.GetIndex("tests.stability", 9_000_000, "COMP-000001", "manufacturing");

        Assert.Equal(first, second);
        Assert.InRange(first, 0, 8_999_999);
    }

    [Fact]
    public void GetIndex_UsesComponentBoundaries()
    {
        Assert.NotEqual(
            StableHash.GetIndex("tests.boundaries", 1_000_000, "ab", "c"),
            StableHash.GetIndex("tests.boundaries", 1_000_000, "a", "bc"));
    }

    [Fact]
    public void GetIndex_SeparatesSemanticDomains()
    {
        Assert.NotEqual(
            StableHash.GetIndex("tests.person-name", 1_000_000, "COMP-000001"),
            StableHash.GetIndex("tests.network-pool", 1_000_000, "COMP-000001"));
    }

    [Fact]
    public void ProductionSource_DoesNotUseProcessSaltedHashApis()
    {
        var sourceRoot = FindSourceRoot();
        var source = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(source, text => text.Contains("HashCode.Combine", StringComparison.Ordinal));
        Assert.DoesNotContain(source, text => text.Contains(".GetHashCode(", StringComparison.Ordinal));
    }

    private static string FindSourceRoot([CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (File.Exists(Path.Combine(candidate, "SyntheticEnterprise.Core", "SyntheticEnterprise.Core.csproj")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the DataGen production source root.");
    }
}
