using System.Collections.Generic;
using System.Linq;
using SyntheticEnterprise.Contracts.Anomalies;

namespace SyntheticEnterprise.Services.Anomalies;

public sealed class AnomalySummaryService : IAnomalySummaryService
{
    public object BuildSummary(IReadOnlyList<NormalizedAnomalyRecord> anomalies)
    {
        return new
        {
            Total = anomalies.Count,
            BySeverity = anomalies.GroupBy(a => a.Severity).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            ByCategory = anomalies.GroupBy(a => a.Category).ToDictionary(g => g.Key, g => g.Count())
        };
    }
}
