namespace SyntheticEnterprise.Contracts.Anomalies;

public sealed class AnomalyEntityReference
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Relationship { get; set; }
}
