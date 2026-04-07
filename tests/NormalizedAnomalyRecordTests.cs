using SyntheticEnterprise.Contracts.Anomalies;
using Xunit;

namespace SyntheticEnterprise.Tests;

public sealed class NormalizedAnomalyRecordTests
{
    [Fact]
    public void Confidence_Should_Be_Within_Expected_Range()
    {
        var anomaly = new NormalizedAnomalyRecord { Confidence = 0.85m };
        Assert.InRange(anomaly.Confidence, 0m, 1m);
    }

    [Fact]
    public void New_Record_Should_Default_To_New_Status()
    {
        var anomaly = new NormalizedAnomalyRecord();
        Assert.Equal(AnomalyStatus.New, anomaly.Status);
    }
}
