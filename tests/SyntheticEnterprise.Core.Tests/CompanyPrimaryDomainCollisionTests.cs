using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Scenarios;

namespace SyntheticEnterprise.Core.Tests;

/// <summary>
/// Two company names that reduce to the same domain label put both directories in one naming
/// context, so every distinguished name below it is minted twice. Account identifiers are issued
/// world-wide and so disambiguate themselves, but a distinguished name states where an object
/// actually sits in a directory and cannot be renamed to dodge the clash. The scenario is therefore
/// refused before generation, and generation refuses it again as a backstop.
/// </summary>
public sealed class CompanyPrimaryDomainCollisionTests
{
    [Fact]
    public void Validator_Rejects_Two_Companies_That_Resolve_To_One_Primary_Domain()
    {
        var result = Validate(
            ("Northwind Trading", "United States"),
            ("North Wind Trading", "United States"));

        Assert.False(result.IsValid);
        var message = Assert.Single(result.Messages, candidate => candidate.Code == "company-primary-domain-collision");
        Assert.Equal(ScenarioValidationSeverity.Error, message.Severity);
        Assert.Equal("$.companies[].name", message.Path);
        Assert.Contains("Northwind Trading", message.Message);
        Assert.Contains("North Wind Trading", message.Message);
        Assert.Contains("northwindtrading.com", message.Message);
    }

    [Fact]
    public void Validator_Names_Every_Company_Sharing_A_Resolved_Primary_Domain()
    {
        var result = Validate(
            ("Northwind Trading", "United States"),
            ("North Wind Trading", "United States"),
            ("North-Wind Trading", "United States"));

        var message = Assert.Single(result.Messages, candidate => candidate.Code == "company-primary-domain-collision");
        Assert.Contains("Northwind Trading", message.Message);
        Assert.Contains("North Wind Trading", message.Message);
        Assert.Contains("North-Wind Trading", message.Message);
    }

    [Fact]
    public void Validator_Accepts_Companies_Whose_Primary_Domains_Are_Distinct()
    {
        var result = Validate(
            ("Northwind Trading", "United States"),
            ("Contoso Freight", "United States"));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Messages, candidate => candidate.Code == "company-primary-domain-collision");
    }

    [Fact]
    public void Validator_Accepts_A_Single_Company()
    {
        var result = Validate(("Northwind Trading", "United States"));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Messages, candidate => candidate.Code == "company-primary-domain-collision");
    }

    [Fact]
    public void Generation_Of_A_Colliding_Scenario_Fails_Loudly_Rather_Than_Emitting_Duplicate_Distinguished_Names()
    {
        var services = new ServiceCollection().AddSyntheticEnterpriseCore().BuildServiceProvider();
        var generator = services.GetRequiredService<IWorldGenerator>();

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(
            new GenerationContext
            {
                Seed = 4242,
                Scenario = new ScenarioDefinition
                {
                    Name = "Colliding Primary Domains",
                    Companies = new()
                    {
                        BuildCompany("Northwind Trading", "United States"),
                        BuildCompany("North Wind Trading", "United States")
                    }
                }
            },
            new CatalogSet()));

        Assert.Contains("duplicate directory account distinguished names", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generated_World_With_Distinct_Primary_Domains_Carries_Unique_Account_Distinguished_Names()
    {
        var services = new ServiceCollection().AddSyntheticEnterpriseCore().BuildServiceProvider();
        var generator = services.GetRequiredService<IWorldGenerator>();

        var world = generator.Generate(
            new GenerationContext
            {
                Seed = 4242,
                Scenario = new ScenarioDefinition
                {
                    Name = "Distinct Primary Domains",
                    Companies = new()
                    {
                        BuildCompany("Northwind Trading", "United States"),
                        BuildCompany("Contoso Freight", "United Kingdom")
                    }
                }
            },
            new CatalogSet()).World;

        var collisions = world.Accounts
            .Select(account => account.DistinguishedName)
            .Where(value => value.Contains('=', StringComparison.Ordinal))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.True(
            collisions.Length == 0,
            $"distinguished names issued more than once: {string.Join(", ", collisions.Take(5))}");
    }

    private static ScenarioValidationResult Validate(params (string Name, string Country)[] companies)
    {
        var services = new ServiceCollection().AddSyntheticEnterpriseCore().BuildServiceProvider();
        var validator = services.GetRequiredService<IScenarioValidator>();

        return validator.Validate(new ScenarioEnvelope
        {
            Name = "Primary Domain Collision",
            Companies = companies.Select(company => BuildCompany(company.Name, company.Country)).ToList()
        });
    }

    private static ScenarioCompanyDefinition BuildCompany(string name, string country)
        => new()
        {
            Name = name,
            EmployeeCount = 40,
            Countries = new() { country }
        };
}
