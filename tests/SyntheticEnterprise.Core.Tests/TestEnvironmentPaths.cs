namespace SyntheticEnterprise.Core.Tests;

internal static class TestEnvironmentPaths
{
    public static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DataGen.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root from the current test assembly path.");
    }

    public static string GetCatalogRoot()
    {
        var catalogRoot = Path.Combine(GetRepositoryRoot(), "catalogs");
        if (!Directory.Exists(catalogRoot))
        {
            throw new DirectoryNotFoundException($"Catalog path not found: {catalogRoot}");
        }

        return catalogRoot;
    }
}
