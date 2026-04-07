using System;
using System.Collections.Generic;

namespace SyntheticEnterprise.Contracts.Anomalies;

public sealed class NormalizedAnomalyRecord
{
    public string AnomalyId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public AnomalySeverity Severity { get; set; } = AnomalySeverity.Medium;
    public decimal Confidence { get; set; } = 0.5m;
    public AnomalyStatus Status { get; set; } = AnomalyStatus.New;
    public string? Rationale { get; set; }
    public string? DetectionHints { get; set; }
    public List<AnomalyRemediationHint> RemediationHints { get; set; } = new();
    public List<AnomalyEvidence> Evidence { get; set; } = new();
    public List<AnomalyEntityReference> TargetEntities { get; set; } = new();
    public List<AnomalyEntityReference> RelatedEntities { get; set; } = new();
    public string SourceLayer { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
