namespace SyntheticEnterprise.Exporting.Contracts;

public sealed class ExportSummary
{
    public int CompanyCount { get; init; }
    public int PersonCount { get; init; }
    public int AccountCount { get; init; }
    public int GroupCount { get; init; }
    public int DeviceCount { get; init; }
    public int RepositoryCount { get; init; }
    public int AnomalyCount { get; init; }
    public int ArtifactCount { get; init; }
}
