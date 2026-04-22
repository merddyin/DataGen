namespace SyntheticEnterprise.Core.Services;

using SyntheticEnterprise.Contracts.Abstractions;

public static class WorldQualityValidationService
{
    private static readonly string[] BlockingMetricKeys =
    [
        "duplicate_person_upns",
        "duplicate_account_upns",
        "companies_missing_identity_metadata",
        "application_counterparty_links_missing_org",
        "process_counterparty_links_missing_org",
        "business_process_configuration_items",
        "undersized_policy_surface"
    ];

    private static readonly string[] AdvisoryMetricKeys =
    [
        "offices_missing_geocode",
        "offices_missing_contact_metadata",
        "people_office_country_mismatch",
        "external_people_missing_employer",
        "guest_accounts_missing_metadata",
        "external_orgs_missing_identity_metadata",
        "external_orgs_missing_relationship_qualifiers",
        "numbered_business_unit_names",
        "numbered_department_names",
        "numbered_team_names",
        "generic_share_names",
        "generic_folder_names",
        "generic_channel_names",
        "office_region_country_mismatch",
        "office_phone_country_mismatch"
    ];

    public static WorldQualityValidationScenarioResult EvaluateScenario(string scenarioPath, int seed, WorldQualityReport quality)
    {
        var dimensionScores = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["realism"] = quality.Realism.Score,
            ["completeness"] = quality.Completeness.Score,
            ["consistency"] = quality.Consistency.Score,
            ["exportability"] = quality.Exportability.Score,
            ["operational"] = quality.Operational.Score
        };

        var blockingMetrics = BlockingMetricKeys
            .Where(key => quality.Metrics.TryGetValue(key, out var value) && value > 0)
            .ToDictionary(key => key, key => quality.Metrics[key], StringComparer.OrdinalIgnoreCase);

        var advisoryMetrics = AdvisoryMetricKeys
            .Where(key => quality.Metrics.TryGetValue(key, out var value) && value > 0)
            .ToDictionary(key => key, key => quality.Metrics[key], StringComparer.OrdinalIgnoreCase);

        var messages = new List<string>();
        if (quality.OverallScore < 80m)
        {
            messages.Add($"Overall quality score {quality.OverallScore} is below the fail threshold of 80.");
        }

        foreach (var dimension in dimensionScores.Where(item => item.Value < 70m))
        {
            messages.Add($"{dimension.Key} score {dimension.Value} is below the fail threshold of 70.");
        }

        foreach (var metric in blockingMetrics)
        {
            messages.Add($"Blocking metric {metric.Key} observed {metric.Value} issue(s).");
        }

        string status;
        if (messages.Count > 0)
        {
            status = "fail";
        }
        else if (quality.OverallScore < 90m
                 || dimensionScores.Any(item => item.Value < 85m)
                 || advisoryMetrics.Count > 0
                 || quality.Warnings.Count > 0)
        {
            status = "warn";

            if (quality.OverallScore < 90m)
            {
                messages.Add($"Overall quality score {quality.OverallScore} is below the pass target of 90.");
            }

            foreach (var dimension in dimensionScores.Where(item => item.Value < 85m))
            {
                messages.Add($"{dimension.Key} score {dimension.Value} is below the pass target of 85.");
            }

            foreach (var metric in advisoryMetrics)
            {
                messages.Add($"Advisory metric {metric.Key} observed {metric.Value} issue(s).");
            }

            if (quality.Warnings.Count > 0)
            {
                messages.Add($"Quality report emitted {quality.Warnings.Count} warning(s).");
            }
        }
        else
        {
            status = "pass";
            messages.Add("Quality validation passed with no blocking or advisory findings.");
        }

        return new WorldQualityValidationScenarioResult
        {
            ScenarioPath = scenarioPath,
            Seed = seed,
            Status = status,
            OverallScore = quality.OverallScore,
            DimensionScores = dimensionScores,
            BlockingMetrics = blockingMetrics,
            AdvisoryMetrics = advisoryMetrics,
            Messages = messages,
            Warnings = quality.Warnings
        };
    }

    public static WorldQualityValidationSummary Summarize(IEnumerable<WorldQualityValidationScenarioResult> scenarios)
    {
        var ordered = scenarios.ToArray();
        var failCount = ordered.Count(item => string.Equals(item.Status, "fail", StringComparison.OrdinalIgnoreCase));
        var warnCount = ordered.Count(item => string.Equals(item.Status, "warn", StringComparison.OrdinalIgnoreCase));
        var passCount = ordered.Count(item => string.Equals(item.Status, "pass", StringComparison.OrdinalIgnoreCase));

        var status = failCount > 0
            ? "fail"
            : warnCount > 0
                ? "warn"
                : "pass";

        return new WorldQualityValidationSummary
        {
            Status = status,
            GeneratedAt = DateTimeOffset.UtcNow,
            ScenarioCount = ordered.Length,
            PassCount = passCount,
            WarnCount = warnCount,
            FailCount = failCount,
            Scenarios = ordered
        };
    }
}
