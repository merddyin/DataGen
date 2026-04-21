using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class TemporalSimulationTests
{
    [Fact]
    public void WorldGenerator_Produces_Deterministic_Timeline_For_Same_Seed()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var generator = services.GetRequiredService<IWorldGenerator>();
        var scenario = BuildScenario();

        var first = generator.Generate(
            new GenerationContext
            {
                Scenario = scenario,
                Seed = 4242
            },
            new CatalogSet());

        var second = generator.Generate(
            new GenerationContext
            {
                Scenario = scenario,
                Seed = 4242
            },
            new CatalogSet());

        var options = new JsonSerializerOptions { WriteIndented = false };
        Assert.Equal(
            JsonSerializer.Serialize(ProjectEvents(first.Temporal.Events), options),
            JsonSerializer.Serialize(ProjectEvents(second.Temporal.Events), options));
        Assert.Equal(
            JsonSerializer.Serialize(first.Temporal.Snapshots, options),
            JsonSerializer.Serialize(second.Temporal.Snapshots, options));
    }

    [Fact]
    public void WorldGenerator_Populates_Timeline_When_Enabled()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var generator = services.GetRequiredService<IWorldGenerator>();

        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario(),
                Seed = 99
            },
            new CatalogSet());

        Assert.True(result.Temporal.Timeline.Enabled);
        Assert.NotEmpty(result.Temporal.Events);
        Assert.NotEmpty(result.Temporal.Snapshots);
        Assert.Contains(result.Temporal.Events, record => record.EventType == "person.hired");
        Assert.Contains(result.Temporal.Events, record => record.EventType == "identity.account_provisioned");
        Assert.Contains(result.Temporal.Events, record => record.EventType == "infrastructure.device_enrolled");
    }

    [Fact]
    public void WorldGenerator_Leaves_Timeline_Empty_When_Disabled()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var generator = services.GetRequiredService<IWorldGenerator>();
        var scenario = BuildScenario() with
        {
            Timeline = new TimelineProfile { Enabled = false }
        };

        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = scenario,
                Seed = 99
            },
            new CatalogSet());

        Assert.False(result.Temporal.Timeline.Enabled);
        Assert.Empty(result.Temporal.Events);
        Assert.Empty(result.Temporal.Snapshots);
    }

    private static ScenarioDefinition BuildScenario()
        => new()
        {
            Name = "Temporal Test",
            Companies = new()
            {
                new ScenarioCompanyDefinition
                {
                    Name = "Temporal Test Co",
                    Industry = "Technology",
                    EmployeeCount = 18,
                    OfficeCount = 2,
                    Countries = new() { "United States" },
                    DatabaseCount = 2,
                    FileShareCount = 2,
                    CollaborationSiteCount = 2,
                    ServerCount = 4
                }
            },
            Timeline = new TimelineProfile
            {
                Enabled = true,
                StartAtUtc = "2026-01-01T00:00:00Z",
                DurationDays = 20,
                SnapshotDays = new() { 0, 10, 20 }
            }
        };

    private static object ProjectEvents(IReadOnlyList<TemporalEventRecord> events)
        => events.Select(record => new
        {
            record.EventType,
            record.EntityType,
            record.OccurredAt
        });
}
