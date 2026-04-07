namespace SyntheticEnterprise.Exporting.Services;

public interface IExportPathResolver
{
    string ResolveRoot(string outputPath, string? artifactPrefix);
}
