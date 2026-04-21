namespace SyntheticEnterprise.Core.Generation;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;

public interface ITemporalSimulationService
{
    TemporalSimulationResult Generate(GenerationContext context, SyntheticEnterpriseWorld world);
}

public sealed class TemporalSimulationService : ITemporalSimulationService
{
    public TemporalSimulationResult Generate(GenerationContext context, SyntheticEnterpriseWorld world)
    {
        var timeline = Normalize(context.Scenario.Timeline);
        if (!timeline.Enabled)
        {
            return new TemporalSimulationResult
            {
                Timeline = timeline
            };
        }

        var startAt = ParseStart(timeline.StartAtUtc);
        var totalDays = Math.Max(1, timeline.DurationDays);
        var random = new Random(context.Seed ?? 0);
        var events = new List<TemporalEventRecord>();

        events.AddRange(BuildPersonEvents(world, startAt, totalDays, random));
        events.AddRange(BuildAccountEvents(world, startAt, totalDays, random));
        events.AddRange(BuildDeviceEvents(world, startAt, totalDays, random));
        events.AddRange(BuildServerEvents(world, startAt, totalDays, random));
        events.AddRange(BuildApplicationEvents(world, startAt, totalDays, random));

        var orderedEvents = events
            .OrderBy(record => record.OccurredAt)
            .ThenBy(record => record.EventType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.EntityType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.EntityId, StringComparer.OrdinalIgnoreCase)
            .Select((record, index) => record with { Id = $"EVT-{index + 1:D6}" })
            .ToList();

        var snapshots = timeline.SnapshotDays
            .Select(day => Math.Clamp(day, 0, totalDays))
            .Distinct()
            .OrderBy(day => day)
            .Select((day, index) =>
            {
                var snapshotAt = startAt.AddDays(day);
                return new TemporalSnapshotDescriptor
                {
                    Id = $"SNP-{index + 1:D3}",
                    Name = day == 0 ? "Initial" : day == totalDays ? "Current" : $"Day {day}",
                    SnapshotAt = snapshotAt,
                    SnapshotMode = timeline.DefaultSnapshotMode,
                    EventCountThroughSnapshot = orderedEvents.Count(record => record.OccurredAt <= snapshotAt)
                };
            })
            .ToList();

        return new TemporalSimulationResult
        {
            Timeline = timeline,
            Events = orderedEvents,
            Snapshots = snapshots
        };
    }

    private static TimelineProfile Normalize(TimelineProfile profile)
    {
        var snapshotDays = profile.SnapshotDays.Count > 0
            ? profile.SnapshotDays.ToList()
            : new List<int> { 0, Math.Max(1, profile.DurationDays) };

        return profile with
        {
            DurationDays = Math.Max(1, profile.DurationDays),
            SnapshotDays = snapshotDays
        };
    }

    private static DateTimeOffset ParseStart(string value)
        => DateTimeOffset.TryParse(value, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    private static IEnumerable<TemporalEventRecord> BuildPersonEvents(
        SyntheticEnterpriseWorld world,
        DateTimeOffset startAt,
        int totalDays,
        Random random)
    {
        return world.People
            .OrderBy(person => person.Id, StringComparer.OrdinalIgnoreCase)
            .Select(person =>
            {
                var hireDay = random.Next(0, totalDays + 1);
                return new TemporalEventRecord
                {
                    EventType = "person.hired",
                    EntityType = "Person",
                    EntityId = person.Id,
                    RelatedEntityType = "Department",
                    RelatedEntityId = person.DepartmentId,
                    OccurredAt = startAt.AddDays(hireDay),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["employment_type"] = person.EmploymentType,
                        ["title"] = person.Title,
                        ["office_id"] = person.OfficeId
                    }
                };
            });
    }

    private static IEnumerable<TemporalEventRecord> BuildAccountEvents(
        SyntheticEnterpriseWorld world,
        DateTimeOffset startAt,
        int totalDays,
        Random random)
    {
        return world.Accounts
            .OrderBy(account => account.Id, StringComparer.OrdinalIgnoreCase)
            .Select(account =>
            {
                var day = random.Next(0, totalDays + 1);
                return new TemporalEventRecord
                {
                    EventType = "identity.account_provisioned",
                    EntityType = "DirectoryAccount",
                    EntityId = account.Id,
                    RelatedEntityType = "Person",
                    RelatedEntityId = account.PersonId,
                    OccurredAt = startAt.AddDays(day).AddMinutes(random.Next(0, 1440)),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["account_type"] = account.AccountType,
                        ["enabled"] = account.Enabled.ToString().ToLowerInvariant(),
                        ["organizational_unit_id"] = account.OuId,
                        ["identity_provider"] = account.IdentityProvider
                    }
                };
            });
    }

    private static IEnumerable<TemporalEventRecord> BuildDeviceEvents(
        SyntheticEnterpriseWorld world,
        DateTimeOffset startAt,
        int totalDays,
        Random random)
    {
        return world.Devices
            .OrderBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .Select(device =>
            {
                var day = random.Next(0, totalDays + 1);
                return new TemporalEventRecord
                {
                    EventType = "infrastructure.device_enrolled",
                    EntityType = "ManagedDevice",
                    EntityId = device.Id,
                    RelatedEntityType = "Person",
                    RelatedEntityId = device.AssignedPersonId,
                    OccurredAt = startAt.AddDays(day).AddMinutes(random.Next(0, 1440)),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["device_type"] = device.DeviceType,
                        ["hostname"] = device.Hostname,
                        ["operating_system"] = device.OperatingSystem
                    }
                };
            });
    }

    private static IEnumerable<TemporalEventRecord> BuildServerEvents(
        SyntheticEnterpriseWorld world,
        DateTimeOffset startAt,
        int totalDays,
        Random random)
    {
        return world.Servers
            .OrderBy(server => server.Id, StringComparer.OrdinalIgnoreCase)
            .Select(server =>
            {
                var day = random.Next(0, totalDays + 1);
                return new TemporalEventRecord
                {
                    EventType = "infrastructure.server_commissioned",
                    EntityType = "ServerAsset",
                    EntityId = server.Id,
                    RelatedEntityType = "Office",
                    RelatedEntityId = server.OfficeId,
                    OccurredAt = startAt.AddDays(day).AddMinutes(random.Next(0, 1440)),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["hostname"] = server.Hostname,
                        ["server_role"] = server.ServerRole,
                        ["environment"] = server.Environment
                    }
                };
            });
    }

    private static IEnumerable<TemporalEventRecord> BuildApplicationEvents(
        SyntheticEnterpriseWorld world,
        DateTimeOffset startAt,
        int totalDays,
        Random random)
    {
        return world.Applications
            .OrderBy(application => application.Id, StringComparer.OrdinalIgnoreCase)
            .Select(application =>
            {
                var day = random.Next(0, totalDays + 1);
                return new TemporalEventRecord
                {
                    EventType = "application.deployed",
                    EntityType = "ApplicationRecord",
                    EntityId = application.Id,
                    RelatedEntityType = "Department",
                    RelatedEntityId = application.OwnerDepartmentId,
                    OccurredAt = startAt.AddDays(day).AddMinutes(random.Next(0, 1440)),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["category"] = application.Category,
                        ["hosting_model"] = application.HostingModel,
                        ["criticality"] = application.Criticality
                    }
                };
            });
    }
}
