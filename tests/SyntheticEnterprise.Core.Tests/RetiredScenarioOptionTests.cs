using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Scenarios;

namespace SyntheticEnterprise.Core.Tests;

public sealed class RetiredScenarioOptionTests
{
    private const string RetiredOptionPath = "$.identity.legacyDirectoryIdentifierVariantCount";

    private static ScenarioOptionRetirement Retirement
        => ScenarioOptionLifecycleRegistry.Default.TryGetRetirement(RetiredOptionPath, out var retirement)
            ? retirement
            : throw new InvalidOperationException($"'{RetiredOptionPath}' is not registered as retired.");

    private static string ScenarioJson(string identityBody) => $$"""
        {
          "name": "Retired Option Scenario",
          "companyCount": 1,
          "employeeSize": { "minimum": 120, "maximum": 180 },
          "identity": { {{identityBody}} }
        }
        """;

    private static IScenarioValidator CreateValidator()
        => new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider()
            .GetRequiredService<IScenarioValidator>();

    private static IScenarioLoader CreateLoader()
        => new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider()
            .GetRequiredService<IScenarioLoader>();

    [Fact]
    public void Validator_Rejects_Scenario_Setting_A_Retired_Option()
    {
        var result = CreateValidator().Validate(ScenarioJson("\"legacyDirectoryIdentifierVariantCount\": 4"));

        Assert.False(result.IsValid);

        var message = Assert.Single(
            result.Messages,
            candidate => candidate.Code == "retired-scenario-option");

        Assert.Equal(ScenarioValidationSeverity.Error, message.Severity);
        Assert.Equal(RetiredOptionPath, message.Path);
    }

    [Fact]
    public void Retired_Option_Message_Names_The_Option_Version_And_Replacement()
    {
        var result = CreateValidator().Validate(ScenarioJson("\"legacyDirectoryIdentifierVariantCount\": 4"));

        var message = Assert.Single(
            result.Messages,
            candidate => candidate.Code == "retired-scenario-option");

        Assert.Contains(RetiredOptionPath, message.Message, StringComparison.Ordinal);
        Assert.Contains(Retirement.RetiredInVersion, message.Message, StringComparison.Ordinal);
        Assert.Contains(
            Retirement.Replacement ?? "Nothing replaces it.",
            message.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_Accepts_The_Same_Scenario_Without_The_Retired_Option()
    {
        var result = CreateValidator().Validate(ScenarioJson("\"includeB2BGuests\": true"));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Messages, message => message.Code == "retired-scenario-option");
    }

    [Fact]
    public void Validator_Accepts_An_Unrelated_Unknown_Property()
    {
        var result = CreateValidator().Validate(ScenarioJson("\"someFutureDirectoryOption\": 7"));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Messages, message => message.Code == "retired-scenario-option");
    }

    [Theory]
    [InlineData("LEGACYDIRECTORYIDENTIFIERVARIANTCOUNT")]
    [InlineData("legacydirectoryidentifiervariantcount")]
    [InlineData("LegacyDirectoryIdentifierVariantCount")]
    public void Retired_Option_Matching_Is_Case_Insensitive(string authoredName)
    {
        var result = CreateValidator().Validate(ScenarioJson($"\"{authoredName}\": 4"));

        Assert.False(result.IsValid);

        var message = Assert.Single(
            result.Messages,
            candidate => candidate.Code == "retired-scenario-option");

        // The canonical registry path is reported regardless of how the option was spelled.
        Assert.Equal(RetiredOptionPath, message.Path);
    }

    [Fact]
    public void Loader_Rejects_A_Scenario_File_Setting_A_Retired_Option()
    {
        var path = Path.Combine(Path.GetTempPath(), $"datagen-retired-option-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, ScenarioJson("\"legacyDirectoryIdentifierVariantCount\": 4"));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => CreateLoader().LoadFromPath(path));

            Assert.Contains(RetiredOptionPath, exception.Message, StringComparison.Ordinal);
            Assert.Contains(Retirement.RetiredInVersion, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loader_Accepts_The_Same_Scenario_File_Without_The_Retired_Option()
    {
        var scenario = CreateLoader().LoadFromJson(ScenarioJson("\"includeB2BGuests\": true"));

        Assert.Equal("Retired Option Scenario", scenario.Name);
        Assert.Single(scenario.Companies);
    }

    [Fact]
    public void Retiring_An_Option_Introduced_By_This_Epic_Is_A_Registry_Change_Only()
    {
        var introduction = Assert.Single(
            ScenarioOptionLifecycleRegistry.Default.Introductions,
            candidate => candidate.Path == "$.infrastructure.effectiveSecurityConfigurationEndpointCount");

        // The forward mechanism: the same inspector rejects a declared option once a retirement row
        // exists for it, with no new code.
        var registry = new ScenarioOptionLifecycleRegistry(
            ScenarioOptionLifecycleRegistry.Default.Introductions.Except(new[] { introduction }),
            ScenarioOptionLifecycleRegistry.Default.Retirements.Append(
                new ScenarioOptionRetirement(introduction.Path, "0.14.0", "a later option")));

        var messages = RetiredScenarioOptionInspector.Inspect(
            """{ "infrastructure": { "effectiveSecurityConfigurationEndpointCount": 12 } }""",
            registry);

        var message = Assert.Single(messages);
        Assert.Equal("retired-scenario-option", message.Code);
        Assert.Equal(ScenarioValidationSeverity.Error, message.Severity);
        Assert.Equal(introduction.Path, message.Path);
        Assert.Contains("0.14.0", message.Message, StringComparison.Ordinal);
        Assert.Contains("a later option", message.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Retirement_Without_A_Replacement_Says_So()
    {
        var registry = new ScenarioOptionLifecycleRegistry(
            Array.Empty<ScenarioOptionIntroduction>(),
            new[] { new ScenarioOptionRetirement("$.identity.someOption", "0.14.0", null) });

        var message = Assert.Single(RetiredScenarioOptionInspector.Inspect(
            """{ "identity": { "someOption": true } }""",
            registry));

        Assert.Contains("Nothing replaces it.", message.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_Option_Is_Both_Introduced_And_Retired()
    {
        var introduced = ScenarioOptionLifecycleRegistry.Default.Introductions.Select(i => i.Path);
        var retired = ScenarioOptionLifecycleRegistry.Default.Retirements.Select(r => r.Path);

        Assert.Empty(introduced.Intersect(retired, StringComparer.OrdinalIgnoreCase));
    }
}
