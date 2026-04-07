namespace SyntheticEnterprise.Contracts.Anomalies;

public sealed class AnomalyRemediationHint
{
    public string Recommendation { get; set; } = string.Empty;
    public string? SuggestedOwnerRole { get; set; }
    public string? EstimatedEffort { get; set; }
    public string? ReferenceKey { get; set; }
}
