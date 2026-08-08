namespace SyntheticEnterprise.Core.Tests;

using System.Reflection;

internal static class TestEnvironmentPaths
{
    public static string GetRepositoryRoot()
    {
        var configuredRoot = typeof(TestEnvironmentPaths).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "DataGenRepositoryRoot")
            ?.Value;
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new DirectoryNotFoundException("The test assembly does not define DataGenRepositoryRoot metadata.");
        }

        var repositoryRoot = Path.GetFullPath(configuredRoot);
        if (!File.Exists(Path.Combine(repositoryRoot, "DataGen.slnx")))
        {
            throw new DirectoryNotFoundException($"Configured repository root is invalid: {repositoryRoot}");
        }

        return repositoryRoot;
    }

    public static string GetCatalogRoot()
    {
        var catalogRoot = Path.Combine(AppContext.BaseDirectory, "catalogs");
        if (!Directory.Exists(catalogRoot))
        {
            throw new DirectoryNotFoundException($"Catalog path not found: {catalogRoot}");
        }

        return catalogRoot;
    }

    public static string GetPluginHostAssemblyPath()
        => Path.Combine(AppContext.BaseDirectory, "SyntheticEnterprise.PluginHost.dll");

    public static string GetPluginHostArtifactsRoot()
    {
        var configuredRoot = typeof(TestEnvironmentPaths).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "DataGenPluginHostArtifactsPath")
            ?.Value;
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new DirectoryNotFoundException(
                "The test assembly does not define DataGenPluginHostArtifactsPath metadata.");
        }

        return Path.GetFullPath(configuredRoot);
    }

    public static string GetPluginHostBuildPathManifest()
        => Path.Combine(AppContext.BaseDirectory, "isolated-plugin-host-source-path.txt");
}
