using System.Collections.Generic;
using SyntheticEnterprise.Contracts.Anomalies;

namespace SyntheticEnterprise.Services.Anomalies;

public interface IAnomalyNormalizationService
{
    IReadOnlyList<NormalizedAnomalyRecord> Normalize(object world);
}
