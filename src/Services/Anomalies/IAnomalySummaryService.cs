using System.Collections.Generic;
using SyntheticEnterprise.Contracts.Anomalies;

namespace SyntheticEnterprise.Services.Anomalies;

public interface IAnomalySummaryService
{
    object BuildSummary(IReadOnlyList<NormalizedAnomalyRecord> anomalies);
}
