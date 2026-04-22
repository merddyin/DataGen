using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Core.Services;

namespace SyntheticEnterprise.Core.Tests;

public sealed class WorldQualityValidationServiceTests
{
    [Fact]
    public void EvaluateScenario_Returns_Fail_For_Blocking_Metrics()
    {
        var report = new WorldQualityReport
        {
            OverallScore = 94m,
            Metrics = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["duplicate_person_upns"] = 1
            },
            Realism = new QualityDimensionReport { Name = "Realism", Score = 94m },
            Completeness = new QualityDimensionReport { Name = "Completeness", Score = 94m },
            Consistency = new QualityDimensionReport { Name = "Consistency", Score = 94m },
            Exportability = new QualityDimensionReport { Name = "Exportability", Score = 94m },
            Operational = new QualityDimensionReport { Name = "Operational", Score = 94m }
        };

        var result = WorldQualityValidationService.EvaluateScenario("example.scenario.json", 4242, report);

        Assert.Equal("fail", result.Status);
        Assert.Contains("duplicate_person_upns", result.BlockingMetrics.Keys);
        Assert.Contains(result.Messages, message => message.Contains("duplicate_person_upns", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateScenario_Returns_Warn_For_Advisory_Metrics()
    {
        var report = new WorldQualityReport
        {
            OverallScore = 92m,
            Metrics = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["office_phone_country_mismatch"] = 2
            },
            Realism = new QualityDimensionReport { Name = "Realism", Score = 92m },
            Completeness = new QualityDimensionReport { Name = "Completeness", Score = 92m },
            Consistency = new QualityDimensionReport { Name = "Consistency", Score = 92m },
            Exportability = new QualityDimensionReport { Name = "Exportability", Score = 92m },
            Operational = new QualityDimensionReport { Name = "Operational", Score = 92m }
        };

        var result = WorldQualityValidationService.EvaluateScenario("example.scenario.json", 7777, report);

        Assert.Equal("warn", result.Status);
        Assert.Contains("office_phone_country_mismatch", result.AdvisoryMetrics.Keys);
        Assert.Contains(result.Messages, message => message.Contains("office_phone_country_mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Summarize_Aggregates_Highest_Severity()
    {
        var summary = WorldQualityValidationService.Summarize(
        [
            new WorldQualityValidationScenarioResult { ScenarioPath = "a", Seed = 1, Status = "pass" },
            new WorldQualityValidationScenarioResult { ScenarioPath = "b", Seed = 2, Status = "warn" },
            new WorldQualityValidationScenarioResult { ScenarioPath = "c", Seed = 3, Status = "fail" }
        ]);

        Assert.Equal("fail", summary.Status);
        Assert.Equal(3, summary.ScenarioCount);
        Assert.Equal(1, summary.PassCount);
        Assert.Equal(1, summary.WarnCount);
        Assert.Equal(1, summary.FailCount);
    }
}
