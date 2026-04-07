namespace SyntheticEnterprise.Contracts.Anomalies;

public sealed class AnomalyEvidence
{
    public string EvidenceId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string? Unit { get; set; }
    public string? Notes { get; set; }
}
