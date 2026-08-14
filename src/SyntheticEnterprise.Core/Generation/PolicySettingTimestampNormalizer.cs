namespace SyntheticEnterprise.Core.Generation;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

/// <summary>
/// Completes source timestamps for generated policy settings without introducing a
/// consumer-side clock dependency. Explicit upstream timestamps are preserved.
/// </summary>
internal static class PolicySettingTimestampNormalizer
{
    public static void Apply(SyntheticEnterpriseWorld world, GenerationContext context)
    {
        var seed = (context.Seed ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var normalized = new PolicySettingRecord[world.PolicySettings.Count];

        for (var index = 0; index < world.PolicySettings.Count; index++)
        {
            normalized[index] = Normalize(world.PolicySettings[index], context, seed);
        }

        for (var index = 0; index < normalized.Length; index++)
        {
            world.PolicySettings[index] = normalized[index];
        }
    }

    private static PolicySettingRecord Normalize(
        PolicySettingRecord setting,
        GenerationContext context,
        string seed)
    {
        var timestamps = new DateTimeOffset?[]
        {
            setting.WhenCreated,
            setting.WhenModified,
            setting.ObservedAtUtc,
            setting.RetrievedAtUtc,
        };
        ValidateExplicitChronology(setting.Id, timestamps);

        var scope = new[] { seed, setting.CompanyId, setting.PolicyId, setting.Id };
        var explicitIndexes = Enumerable.Range(0, timestamps.Length)
            .Where(index => timestamps[index].HasValue)
            .ToArray();
        if (explicitIndexes.Length == 0)
        {
            timestamps[3] = context.GeneratedAt.ToUniversalTime();
            timestamps[2] = SubtractClamped(timestamps[3]!.Value, LagBefore(3, scope));
            timestamps[1] = SubtractClamped(timestamps[2]!.Value, LagBefore(2, scope));
            timestamps[0] = SubtractClamped(timestamps[1]!.Value, LagBefore(1, scope));
        }
        else
        {
            var firstExplicit = explicitIndexes[0];
            for (var index = firstExplicit - 1; index >= 0; index--)
            {
                timestamps[index] = SubtractClamped(timestamps[index + 1]!.Value, LagBefore(index + 1, scope));
            }

            for (var explicitIndex = 0; explicitIndex < explicitIndexes.Length - 1; explicitIndex++)
            {
                FillBetween(
                    timestamps,
                    explicitIndexes[explicitIndex],
                    explicitIndexes[explicitIndex + 1]);
            }

            var lastExplicit = explicitIndexes[^1];
            if (lastExplicit < timestamps.Length - 1)
            {
                var lower = timestamps[lastExplicit]!.Value;
                var upper = context.GeneratedAt < lower
                    ? lower
                    : context.GeneratedAt.ToUniversalTime();
                FillAfter(timestamps, lastExplicit, upper);
            }
        }

        return setting with
        {
            WhenCreated = timestamps[0],
            WhenModified = timestamps[1],
            ObservedAtUtc = timestamps[2],
            RetrievedAtUtc = timestamps[3],
        };
    }

    private static void ValidateExplicitChronology(
        string settingId,
        IReadOnlyList<DateTimeOffset?> timestamps)
    {
        DateTimeOffset? previous = null;
        foreach (var timestamp in timestamps)
        {
            if (!timestamp.HasValue)
            {
                continue;
            }

            if (previous > timestamp)
            {
                throw new InvalidOperationException(
                    $"Policy setting '{settingId}' has an invalid source timestamp order.");
            }

            previous = timestamp;
        }
    }

    private static void FillBetween(
        IList<DateTimeOffset?> timestamps,
        int lowerIndex,
        int upperIndex)
    {
        var lower = timestamps[lowerIndex]!.Value;
        var upper = timestamps[upperIndex]!.Value;
        var segments = upperIndex - lowerIndex;
        for (var index = lowerIndex + 1; index < upperIndex; index++)
        {
            timestamps[index] = Interpolate(lower, upper, index - lowerIndex, segments);
        }
    }

    private static void FillAfter(
        IList<DateTimeOffset?> timestamps,
        int lowerIndex,
        DateTimeOffset upper)
    {
        var lower = timestamps[lowerIndex]!.Value;
        var segments = timestamps.Count - 1 - lowerIndex;
        for (var index = lowerIndex + 1; index < timestamps.Count; index++)
        {
            timestamps[index] = Interpolate(lower, upper, index - lowerIndex, segments);
        }
    }

    private static DateTimeOffset Interpolate(
        DateTimeOffset lower,
        DateTimeOffset upper,
        int position,
        int segments)
    {
        var lowerTicks = lower.UtcTicks;
        var tickOffset = decimal.ToInt64(
            decimal.Truncate((upper.UtcTicks - lowerTicks) * (decimal)position / segments));
        return new DateTimeOffset(lowerTicks + tickOffset, TimeSpan.Zero);
    }

    private static TimeSpan LagBefore(int upperIndex, string[] scope) => upperIndex switch
    {
        3 => TimeSpan.FromHours(StableHash.GetIndex("policy-setting-observed-age-hours", 72, scope)),
        2 => TimeSpan.FromDays(1 + StableHash.GetIndex("policy-setting-modified-age-days", 45, scope)),
        _ => TimeSpan.FromDays(30 + StableHash.GetIndex("policy-setting-created-age-days", 1095, scope)),
    };

    private static DateTimeOffset SubtractClamped(DateTimeOffset value, TimeSpan amount)
    {
        var minimum = DateTimeOffset.MinValue;
        var resultTicks = value.UtcTicks - minimum.UtcTicks < amount.Ticks
            ? minimum.UtcTicks
            : value.UtcTicks - amount.Ticks;
        return new DateTimeOffset(resultTicks, TimeSpan.Zero);
    }
}
