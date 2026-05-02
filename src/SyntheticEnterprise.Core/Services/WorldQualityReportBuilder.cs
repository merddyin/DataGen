namespace SyntheticEnterprise.Core.Services;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Core.Abstractions;

internal static class WorldQualityReportBuilder
{
    private static readonly string[] RealismMetricKeys =
    [
        "numbered_business_unit_names",
        "numbered_department_names",
        "numbered_team_names",
        "generic_share_names",
        "generic_folder_names",
        "generic_channel_names",
        "undersized_policy_surface",
        "office_region_country_mismatch",
        "office_phone_country_mismatch"
    ];

    private static readonly string[] CompletenessMetricKeys =
    [
        "companies_missing_identity_metadata",
        "offices_missing_geocode",
        "offices_missing_contact_metadata",
        "external_people_missing_employer",
        "guest_accounts_missing_metadata",
        "external_orgs_missing_identity_metadata",
        "external_orgs_missing_relationship_qualifiers"
    ];

    private static readonly string[] ConsistencyMetricKeys =
    [
        "people_office_country_mismatch",
        "duplicate_person_upns",
        "duplicate_account_upns",
        "duplicate_account_sam_account_names",
        "duplicate_generated_passwords",
        "duplicate_external_org_names"
    ];

    private static readonly string[] ExportabilityMetricKeys =
    [
        "application_counterparty_links_missing_org",
        "process_counterparty_links_missing_org",
        "business_process_configuration_items"
    ];

    public static WorldQualityReport Build(
        WorldQualityAuditResult audit,
        GenerationStatistics statistics,
        WorldMetadata? worldMetadata,
        TemporalSimulationResult temporal)
    {
        var realism = BuildRealismDimension(audit);
        var completeness = BuildCompletenessDimension(audit);
        var consistency = BuildConsistencyDimension(audit);
        var exportability = BuildExportabilityDimension(audit);
        var operational = BuildOperationalDimension(audit, statistics, worldMetadata, temporal);
        var warnings = audit.Warnings.Concat(operational.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var heuristics = realism.Inputs
            .Concat(completeness.Inputs)
            .Concat(consistency.Inputs)
            .Concat(exportability.Inputs)
            .Concat(operational.Inputs)
            .Select(input => $"{input.Label}: {input.Heuristic}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WorldQualityReport
        {
            OverallScore = decimal.Round(new[] { realism.Score, completeness.Score, consistency.Score, exportability.Score, operational.Score }.Average(), 2),
            Warnings = warnings,
            Heuristics = heuristics,
            Metrics = audit.Metrics,
            Samples = audit.Samples,
            Realism = realism,
            Completeness = completeness,
            Consistency = consistency,
            Exportability = exportability,
            Operational = operational
        };
    }

    private static QualityDimensionReport BuildRealismDimension(WorldQualityAuditResult audit)
        => BuildDimension(
            "Realism",
            audit,
            RealismMetricKeys,
            new[]
            {
                IssueInput("numbered_business_unit_names", "Numbered business unit names", 5m, "count", "Business units should be contextualized, not suffixed numerically."),
                IssueInput("numbered_department_names", "Numbered department names", 6m, "count", "Departments should avoid synthetic numeric uniqueness markers."),
                IssueInput("numbered_team_names", "Numbered team names", 6m, "count", "Teams should use role or scope context instead of trailing numbers."),
                IssueInput("generic_share_names", "Generic file share names", 8m, "count", "Department and team shares should use natural enterprise naming patterns."),
                IssueInput("generic_folder_names", "Generic document folder names", 6m, "count", "Repository folder trees should look curated rather than sequenced."),
                IssueInput("generic_channel_names", "Generic collaboration channel names", 5m, "count", "Collaboration channels should reflect team purpose instead of template defaults."),
                IssueInput("undersized_policy_surface", "Undersized policy surface", 18m, "flag", "Large enterprises should expose a layered management surface across policies and settings."),
                IssueInput("office_region_country_mismatch", "Office region/country mismatch", 6m, "count", "Known international office countries should align with realistic regional labels."),
                IssueInput("office_phone_country_mismatch", "Office phone/country mismatch", 8m, "count", "Known international office countries should use country-appropriate business phone prefixes."),
                CoverageInput("policy_count", "Policy coverage", 12m, ResolveExpectedMinimumPolicies(audit), "count", "Policy volume should scale with organizational size and segmentation."),
                CoverageInput("policy_setting_count", "Policy setting coverage", 20m, ResolveExpectedMinimumPolicySettings(audit), "count", "Policy settings should reflect layered baselines, exceptions, and management overlap."),
                CoverageInput("group_count", "Group coverage", 14m, ResolveExpectedMinimumGroups(audit), "count", "Enterprise access models need a broad group surface for users, resources, and delegation.")
            });

    private static QualityDimensionReport BuildCompletenessDimension(WorldQualityAuditResult audit)
        => BuildDimension(
            "Completeness",
            audit,
            CompletenessMetricKeys,
            new[]
            {
                IssueInput("companies_missing_identity_metadata", "Companies missing identity metadata", 10m, "count", "Companies should have domain, website, contact, and headquarters identity."),
                IssueInput("offices_missing_geocode", "Offices missing geocode", 10m, "count", "Office records should include postal and mapping details for downstream joins."),
                IssueInput("offices_missing_contact_metadata", "Offices missing contact metadata", 8m, "count", "Office records should carry business contact information and headquarters markers."),
                IssueInput("external_people_missing_employer", "External workforce missing employer", 8m, "count", "Contractors and partner workers should tie back to an employer organization."),
                IssueInput("guest_accounts_missing_metadata", "Guest accounts missing metadata", 12m, "count", "Guest/B2B accounts should include invitation, tenant, sponsor, and governance details."),
                IssueInput("external_orgs_missing_identity_metadata", "External organizations missing identity metadata", 8m, "count", "Materialized counterparties should expose basic identifying metadata."),
                IssueInput("external_orgs_missing_relationship_qualifiers", "External organizations missing relationship qualifiers", 10m, "count", "Counterparty records should explain why they exist and what the relationship means."),
                CoverageInput("company_count", "Company surface coverage", 8m, 1m, "count", "A generated world should materialize at least one root company."),
                CoverageInput("file_share_count", "Repository surface coverage", 8m, 1m, "count", "Repository layers should produce at least one accessible enterprise repository."),
                CoverageInput("collaboration_site_count", "Collaboration site coverage", 8m, 1m, "count", "Modern enterprise footprints should include collaboration surfaces.")
            });

    private static QualityDimensionReport BuildConsistencyDimension(WorldQualityAuditResult audit)
        => BuildDimension(
            "Consistency",
            audit,
            ConsistencyMetricKeys,
            new[]
            {
                IssueInput("people_office_country_mismatch", "People/office country mismatch", 10m, "count", "People should align with the country of their assigned office."),
                IssueInput("duplicate_person_upns", "Duplicate person UPNs", 16m, "count", "People should not share user principal names."),
                IssueInput("duplicate_account_upns", "Duplicate account UPNs", 16m, "count", "Directory accounts should not share user principal names."),
                IssueInput("duplicate_account_sam_account_names", "Duplicate account sAMAccountNames", 16m, "count", "Directory accounts should not share sAMAccountName values within the same directory surface."),
                IssueInput("duplicate_generated_passwords", "Duplicate generated passwords", 8m, "count", "Generated passwords should not collide across accounts."),
                IssueInput("duplicate_external_org_names", "Duplicate external organization names", 8m, "count", "Counterparty names should be unique enough to avoid graph ambiguity.")
            });

    private static QualityDimensionReport BuildExportabilityDimension(WorldQualityAuditResult audit)
        => BuildDimension(
            "Exportability",
            audit,
            ExportabilityMetricKeys,
            new[]
            {
                IssueInput("application_counterparty_links_missing_org", "Application counterparty links missing orgs", 14m, "count", "App-to-counterparty links should resolve to materialized organizations."),
                IssueInput("process_counterparty_links_missing_org", "Process counterparty links missing orgs", 14m, "count", "Process-to-counterparty links should resolve to materialized organizations."),
                IssueInput("business_process_configuration_items", "Business processes projected as CIs", 18m, "count", "Business processes should not be exported as configuration items.")
            });

    private static QualityDimensionReport BuildOperationalDimension(
        WorldQualityAuditResult audit,
        GenerationStatistics statistics,
        WorldMetadata? worldMetadata,
        TemporalSimulationResult temporal)
    {
        var appliedLayers = worldMetadata?.AppliedLayers?.Count ?? 0;
        var enabledPackIds = (worldMetadata?.Scenario.Packs?.EnabledPacks ?? [])
            .Where(pack => pack.Enabled && !string.IsNullOrWhiteSpace(pack.PackId))
            .Select(pack => pack.PackId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var packSelections = enabledPackIds.Length;
        var temporalEvents = temporal.Events.Count;
        var temporalSnapshots = temporal.Snapshots.Count;
        var generatedPackCapabilities = audit.Samples.TryGetValue("plugin_capabilities", out var capabilities)
            ? capabilities.Where(capability => !string.IsNullOrWhiteSpace(capability)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materializedPackCount = enabledPackIds.Count(generatedPackCapabilities.Contains);
        var missingEnabledPackIds = enabledPackIds.Where(packId => !generatedPackCapabilities.Contains(packId)).ToArray();
        var operationalWarnings = missingEnabledPackIds.Length > 0
            ? new[]
            {
                $"World quality audit: enabled scenario packs did not materialize generated records for {string.Join(", ", missingEnabledPackIds)}."
            }
            : Array.Empty<string>();

        var inputs = new[]
        {
            CoverageInputFromValue("layer_coverage", "Applied layer coverage", 25m, appliedLayers, 3m, "layers", "A generated world should retain multiple applied layers for traceability across domains."),
            CoverageInputFromValue("temporal_event_coverage", "Temporal event coverage", 20m, temporalEvents, 1m, "events", "Temporal simulations should emit events when timeline services are enabled."),
            CoverageInputFromValue("temporal_snapshot_coverage", "Temporal snapshot coverage", 10m, temporalSnapshots, 1m, "snapshots", "Temporal runs should retain at least one snapshot for point-in-time inspection."),
            CoverageInputFromValue("pack_selection_traceability", "Pack selection traceability", 10m, packSelections, packSelections > 0 ? 1m : 0m, "packs", "When scenarios opt into packs, the quality report should capture that activation context."),
            CoverageInputFromValue("enabled_pack_artifact_coverage", "Enabled pack artifact coverage", 15m, materializedPackCount, packSelections, "packs", "Enabled scenario packs should materialize generated records or relationships in the resulting world."),
            CoverageInputFromValue("application_surface", "Application surface coverage", 10m, statistics.ApplicationCount, 1m, "applications", "Generated worlds should retain at least one application surface."),
            CoverageInputFromValue("device_surface", "Device surface coverage", 10m, statistics.DeviceCount, 1m, "devices", "Generated worlds should retain at least one endpoint or server surface."),
            CoverageInputFromValue("repository_surface", "Repository surface coverage", 10m, statistics.RepositoryCount, 1m, "repositories", "Generated worlds should retain at least one repository surface."),
            CoverageInputFromValue("account_surface", "Identity surface coverage", 5m, statistics.AccountCount, 1m, "accounts", "Generated worlds should retain at least one account surface.")
        };

        return BuildDimension(
            "Operational",
            operationalWarnings,
            Array.Empty<string>(),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            inputs);
    }

    private static QualityDimensionReport BuildDimension(
        string name,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> metricKeys,
        IReadOnlyDictionary<string, int> metrics,
        IReadOnlyList<QualityScoreInput> inputs)
    {
        var score = decimal.Round(decimal.Max(0m, 100m - inputs.Sum(input => input.Penalty)), 2);
        var filteredWarnings = warnings
            .Where(warning => metricKeys.Any(metricKey => WarningMatchesMetric(warning, metricKey)))
            .ToArray();

        return new QualityDimensionReport
        {
            Name = name,
            Score = score,
            MaxScore = 100m,
            FindingCount = metricKeys.Sum(metricKey => metrics.TryGetValue(metricKey, out var value) ? value : 0),
            MetricKeys = metricKeys.ToArray(),
            Warnings = filteredWarnings,
            Inputs = inputs
        };
    }

    private static QualityDimensionReport BuildDimension(
        string name,
        WorldQualityAuditResult audit,
        IReadOnlyList<string> metricKeys,
        params Func<IReadOnlyDictionary<string, int>, QualityScoreInput>[] inputFactories)
        => BuildDimension(name, audit.Warnings, metricKeys, audit.Metrics, inputFactories.Select(factory => factory(audit.Metrics)).ToArray());

    private static Func<IReadOnlyDictionary<string, int>, QualityScoreInput> IssueInput(string key, string label, decimal weight, string unit, string heuristic)
        => metrics =>
        {
            var observedValue = metrics.TryGetValue(key, out var value) ? value : 0;
            var penalty = observedValue > 0 ? weight : 0m;
            return new QualityScoreInput
            {
                Key = key,
                Label = label,
                Category = "Issue",
                ObservedValue = observedValue,
                TargetValue = 0m,
                Weight = weight,
                Penalty = penalty,
                Passing = observedValue == 0,
                Unit = unit,
                Heuristic = heuristic
            };
        };

    private static Func<IReadOnlyDictionary<string, int>, QualityScoreInput> CoverageInput(string key, string label, decimal weight, decimal targetValue, string unit, string heuristic)
        => metrics => CoverageInputFromValue(
            key,
            label,
            weight,
            metrics.TryGetValue(key, out var value) ? value : 0,
            targetValue,
            unit,
            heuristic);

    private static QualityScoreInput CoverageInputFromValue(string key, string label, decimal weight, decimal observedValue, decimal targetValue, string unit, string heuristic)
    {
        var ratio = targetValue <= 0m ? 1m : Math.Min(1m, observedValue / targetValue);
        var penalty = decimal.Round(weight * (1m - ratio), 2);
        return new QualityScoreInput
        {
            Key = key,
            Label = label,
            Category = "Coverage",
            ObservedValue = observedValue,
            TargetValue = targetValue,
            Weight = weight,
            Penalty = penalty,
            Passing = penalty == 0m,
            Unit = unit,
            Heuristic = heuristic
        };
    }

    private static decimal ResolveExpectedMinimumPolicies(WorldQualityAuditResult audit)
        => ResolveExpectedMinimum(
            audit,
            large: 100m,
            mediumLarge: 80m,
            medium: 60m,
            small: 40m,
            smallest: 20m);

    private static decimal ResolveExpectedMinimumPolicySettings(WorldQualityAuditResult audit)
        => ResolveExpectedMinimum(
            audit,
            large: 1500m,
            mediumLarge: 1100m,
            medium: 800m,
            small: 500m,
            smallest: 250m);

    private static decimal ResolveExpectedMinimumGroups(WorldQualityAuditResult audit)
        => ResolveExpectedMinimum(
            audit,
            large: 350m,
            mediumLarge: 220m,
            medium: 120m,
            small: 60m,
            smallest: 20m);

    private static decimal ResolveExpectedMinimum(
        WorldQualityAuditResult audit,
        decimal large,
        decimal mediumLarge,
        decimal medium,
        decimal small,
        decimal smallest)
    {
        var employeeCount = audit.Metrics.TryGetValue("person_count", out var explicitCount)
            ? explicitCount
            : 0;

        return employeeCount switch
        {
            >= 10000 => large,
            >= 5000 => mediumLarge,
            >= 1000 => medium,
            >= 250 => small,
            _ => smallest
        };
    }

    private static bool WarningMatchesMetric(string warning, string metricKey)
        => metricKey switch
        {
            "numbered_business_unit_names" => warning.Contains("business unit names", StringComparison.OrdinalIgnoreCase),
            "numbered_department_names" => warning.Contains("department names", StringComparison.OrdinalIgnoreCase),
            "numbered_team_names" => warning.Contains("team names", StringComparison.OrdinalIgnoreCase),
            "generic_share_names" => warning.Contains("file shares", StringComparison.OrdinalIgnoreCase),
            "generic_folder_names" => warning.Contains("document folders", StringComparison.OrdinalIgnoreCase),
            "generic_channel_names" => warning.Contains("collaboration channel names", StringComparison.OrdinalIgnoreCase),
            "undersized_policy_surface" => warning.Contains("policy and policy-setting counts", StringComparison.OrdinalIgnoreCase),
            "companies_missing_identity_metadata" => warning.Contains("companies are missing", StringComparison.OrdinalIgnoreCase),
            "offices_missing_geocode" => warning.Contains("postal/geocode", StringComparison.OrdinalIgnoreCase),
            "offices_missing_contact_metadata" => warning.Contains("business phone numbers", StringComparison.OrdinalIgnoreCase),
            "external_people_missing_employer" => warning.Contains("external workforce people", StringComparison.OrdinalIgnoreCase),
            "guest_accounts_missing_metadata" => warning.Contains("guest or B2B accounts", StringComparison.OrdinalIgnoreCase),
            "external_orgs_missing_identity_metadata" => warning.Contains("external organizations are missing legal", StringComparison.OrdinalIgnoreCase),
            "external_orgs_missing_relationship_qualifiers" => warning.Contains("external organizations are missing relationship basis", StringComparison.OrdinalIgnoreCase),
            "people_office_country_mismatch" => warning.Contains("country values", StringComparison.OrdinalIgnoreCase),
            "duplicate_person_upns" => warning.Contains("duplicate person user principal", StringComparison.OrdinalIgnoreCase),
            "duplicate_account_upns" => warning.Contains("duplicate directory account user principal", StringComparison.OrdinalIgnoreCase),
            "duplicate_generated_passwords" => warning.Contains("duplicate generated passwords", StringComparison.OrdinalIgnoreCase),
            "duplicate_external_org_names" => warning.Contains("duplicate external organization names", StringComparison.OrdinalIgnoreCase),
            "application_counterparty_links_missing_org" => warning.Contains("application counterparty links", StringComparison.OrdinalIgnoreCase),
            "process_counterparty_links_missing_org" => warning.Contains("business process counterparty links", StringComparison.OrdinalIgnoreCase),
            "business_process_configuration_items" => warning.Contains("business processes are surfacing as configuration items", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}
