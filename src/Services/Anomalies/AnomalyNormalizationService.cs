using System.Collections.Generic;
using SyntheticEnterprise.Contracts.Anomalies;

namespace SyntheticEnterprise.Services.Anomalies;

public sealed class AnomalyNormalizationService : IAnomalyNormalizationService
{
    public IReadOnlyList<NormalizedAnomalyRecord> Normalize(object world)
    {
        // Scaffold only: adapt legacy anomaly collections from the world object
        // into the shared NormalizedAnomalyRecord shape.
        return new List<NormalizedAnomalyRecord>();
    }
}
