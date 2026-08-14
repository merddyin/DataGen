using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Generation;

namespace SyntheticEnterprise.Core.Tests;

public sealed class PolicySettingTimestampNormalizerTests
{
    [Fact]
    public void Apply_PreservesValidExplicitSourceTimestamps()
    {
        var world = CreateWorld(new PolicySettingRecord
        {
            Id = "PST-EXPLICIT",
            CompanyId = "CO-001",
            PolicyId = "POL-001",
            SettingName = "RequireEncryption",
            WhenCreated = DateTimeOffset.Parse("2025-01-10T08:00:00Z"),
            WhenModified = DateTimeOffset.Parse("2025-06-15T09:30:00Z"),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-20T14:00:00Z"),
            RetrievedAtUtc = DateTimeOffset.Parse("2026-07-20T14:05:00Z"),
        });

        PolicySettingTimestampNormalizer.Apply(world, CreateContext());

        var setting = Assert.Single(world.PolicySettings);
        Assert.Equal(DateTimeOffset.Parse("2025-01-10T08:00:00Z"), setting.WhenCreated);
        Assert.Equal(DateTimeOffset.Parse("2025-06-15T09:30:00Z"), setting.WhenModified);
        Assert.Equal(DateTimeOffset.Parse("2026-07-20T14:00:00Z"), setting.ObservedAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-07-20T14:05:00Z"), setting.RetrievedAtUtc);
    }

    [Fact]
    public void Apply_RejectsInvalidExplicitOrderingWithoutRewritingSourceTimestamps()
    {
        var world = CreateWorld(new PolicySettingRecord
        {
            Id = "PST-INVALID",
            CompanyId = "CO-001",
            PolicyId = "POL-001",
            SettingName = "RequireEncryption",
            WhenCreated = DateTimeOffset.Parse("2025-06-16T09:30:00Z"),
            WhenModified = DateTimeOffset.Parse("2025-06-15T09:30:00Z"),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-20T14:00:00Z"),
            RetrievedAtUtc = DateTimeOffset.Parse("2026-07-20T14:05:00Z"),
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PolicySettingTimestampNormalizer.Apply(world, CreateContext()));

        Assert.Contains("PST-INVALID", exception.Message, StringComparison.Ordinal);
        var setting = Assert.Single(world.PolicySettings);
        Assert.Equal(DateTimeOffset.Parse("2025-06-16T09:30:00Z"), setting.WhenCreated);
        Assert.Equal(DateTimeOffset.Parse("2025-06-15T09:30:00Z"), setting.WhenModified);
        Assert.Equal(DateTimeOffset.Parse("2026-07-20T14:00:00Z"), setting.ObservedAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-07-20T14:05:00Z"), setting.RetrievedAtUtc);
    }

    [Fact]
    public void Apply_FillsEveryPartialExplicitTimestampPermutationWithinNeighboringBounds()
    {
        var explicitValues = new DateTimeOffset[]
        {
            DateTimeOffset.Parse("2026-07-20T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-20T11:00:00Z"),
            DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
            DateTimeOffset.Parse("2026-07-20T13:00:00Z"),
        };

        for (var mask = 1; mask < 15; mask++)
        {
            var original = CreatePartialSetting($"PST-{mask:00}", mask, explicitValues);
            var first = CreateWorld(original);
            var replay = CreateWorld(original);

            PolicySettingTimestampNormalizer.Apply(first, CreateContext());
            PolicySettingTimestampNormalizer.Apply(replay, CreateContext());

            var normalized = Assert.Single(first.PolicySettings);
            Assert.Equal(normalized, Assert.Single(replay.PolicySettings));
            var actual = GetTimestamps(normalized);
            Assert.All(actual, timestamp => Assert.NotNull(timestamp));
            Assert.True(actual[0] <= actual[1]);
            Assert.True(actual[1] <= actual[2]);
            Assert.True(actual[2] <= actual[3]);

            var supplied = GetTimestamps(original);
            for (var index = 0; index < supplied.Length; index++)
            {
                if (supplied[index].HasValue)
                {
                    Assert.Equal(supplied[index], actual[index]);
                }
            }
        }
    }

    [Fact]
    public void Apply_WhenAnySettingFails_LeavesEntireCollectionUntouched()
    {
        var first = new PolicySettingRecord
        {
            Id = "PST-FIRST",
            CompanyId = "CO-001",
            PolicyId = "POL-001",
            SettingName = "First",
        };
        var invalid = new PolicySettingRecord
        {
            Id = "PST-INVALID",
            CompanyId = "CO-001",
            PolicyId = "POL-001",
            SettingName = "Invalid",
            WhenCreated = DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-20T11:00:00Z"),
        };
        var world = CreateWorld(first, invalid);
        var before = world.PolicySettings.ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            PolicySettingTimestampNormalizer.Apply(world, CreateContext()));

        Assert.Equal(before, world.PolicySettings);
    }

    private static SyntheticEnterpriseWorld CreateWorld(params PolicySettingRecord[] settings)
    {
        var world = new SyntheticEnterpriseWorld();
        world.PolicySettings.AddRange(settings);
        return world;
    }

    private static PolicySettingRecord CreatePartialSetting(
        string id,
        int mask,
        IReadOnlyList<DateTimeOffset> values)
        => new()
        {
            Id = id,
            CompanyId = "CO-001",
            PolicyId = "POL-001",
            SettingName = "Partial timestamps",
            WhenCreated = (mask & 1) != 0 ? values[0] : null,
            WhenModified = (mask & 2) != 0 ? values[1] : null,
            ObservedAtUtc = (mask & 4) != 0 ? values[2] : null,
            RetrievedAtUtc = (mask & 8) != 0 ? values[3] : null,
        };

    private static DateTimeOffset?[] GetTimestamps(PolicySettingRecord setting) =>
    [
        setting.WhenCreated,
        setting.WhenModified,
        setting.ObservedAtUtc,
        setting.RetrievedAtUtc,
    ];

    private static GenerationContext CreateContext() => new()
    {
        Scenario = new ScenarioDefinition { Name = "Policy timestamp contract" },
        Seed = 1130,
        GeneratedAt = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
    };
}
