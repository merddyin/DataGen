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
    private static readonly string[] TerminationReasons =
    {
        "Voluntary",
        "Restructuring",
        "Performance",
        "Role Elimination"
    };

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
        var personPlans = BuildPersonPlans(world, totalDays, random);

        events.AddRange(BuildPersonEvents(personPlans, startAt, random));
        events.AddRange(BuildAccountEvents(world, personPlans, startAt, totalDays, random));
        events.AddRange(BuildDeviceEvents(world, personPlans, startAt, totalDays, random));
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

    private static IReadOnlyDictionary<string, PersonLifecyclePlan> BuildPersonPlans(
        SyntheticEnterpriseWorld world,
        int totalDays,
        Random random)
    {
        var departmentsByCompany = world.Departments
            .GroupBy(department => department.CompanyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var teamsByDepartment = world.Teams
            .GroupBy(team => team.DepartmentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var officesByCompany = world.Offices
            .GroupBy(office => office.CompanyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

        var plans = new Dictionary<string, PersonLifecyclePlan>(StringComparer.OrdinalIgnoreCase);
        var orderedPeople = world.People.OrderBy(person => person.Id, StringComparer.OrdinalIgnoreCase).ToList();

        for (var index = 0; index < orderedPeople.Count; index++)
        {
            var person = orderedPeople[index];
            var hireDay = random.Next(0, Math.Max(2, totalDays / 3) + 1);
            var transferDay = default(int?);
            var promotionDay = default(int?);
            var terminationDay = default(int?);
            var previousDepartmentId = default(string);
            var previousTeamId = default(string);
            var previousOfficeId = default(string);
            var previousTitle = default(string);

            if (ShouldTransfer(person, index, totalDays))
            {
                transferDay = ClampDay(hireDay + random.Next(3, Math.Max(4, totalDays / 2) + 1), totalDays);
                previousDepartmentId = SelectAlternativeDepartmentId(person, departmentsByCompany);
                previousTeamId = SelectAlternativeTeamId(person, teamsByDepartment, previousDepartmentId);
                previousOfficeId = SelectAlternativeOfficeId(person, officesByCompany);
            }

            if (ShouldPromote(person, index, totalDays))
            {
                promotionDay = ClampDay(hireDay + random.Next(5, Math.Max(6, totalDays / 2) + 1), totalDays);
                previousTitle = InferPreviousTitle(person.Title);
            }

            if (ShouldTerminate(person, index, totalDays))
            {
                terminationDay = ClampDay(hireDay + random.Next(7, Math.Max(8, totalDays) + 1), totalDays);
            }

            if (terminationDay.HasValue)
            {
                if (transferDay.HasValue && transferDay.Value >= terminationDay.Value)
                {
                    transferDay = Math.Max(hireDay + 1, terminationDay.Value - 1);
                }

                if (promotionDay.HasValue && promotionDay.Value >= terminationDay.Value)
                {
                    promotionDay = Math.Max(hireDay + 1, terminationDay.Value - 1);
                }
            }

            plans[person.Id] = new PersonLifecyclePlan(
                person,
                hireDay,
                transferDay,
                promotionDay,
                terminationDay,
                previousDepartmentId,
                previousTeamId,
                previousOfficeId,
                previousTitle);
        }

        return plans;
    }

    private static IEnumerable<TemporalEventRecord> BuildPersonEvents(
        IReadOnlyDictionary<string, PersonLifecyclePlan> personPlans,
        DateTimeOffset startAt,
        Random random)
    {
        foreach (var plan in personPlans.Values.OrderBy(item => item.Person.Id, StringComparer.OrdinalIgnoreCase))
        {
            yield return new TemporalEventRecord
            {
                EventType = "person.hired",
                EntityType = "Person",
                EntityId = plan.Person.Id,
                RelatedEntityType = "Department",
                RelatedEntityId = plan.Person.DepartmentId,
                OccurredAt = startAt.AddDays(plan.HireDay),
                Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["employment_type"] = plan.Person.EmploymentType,
                    ["title"] = plan.Person.Title,
                    ["office_id"] = plan.Person.OfficeId
                }
            };

            if (plan.TransferDay.HasValue)
            {
                yield return new TemporalEventRecord
                {
                    EventType = "person.transferred",
                    EntityType = "Person",
                    EntityId = plan.Person.Id,
                    RelatedEntityType = "Department",
                    RelatedEntityId = plan.Person.DepartmentId,
                    OccurredAt = startAt.AddDays(plan.TransferDay.Value).AddMinutes(random.Next(60, 480)),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["previous_department_id"] = plan.PreviousDepartmentId,
                        ["current_department_id"] = plan.Person.DepartmentId,
                        ["previous_team_id"] = plan.PreviousTeamId,
                        ["current_team_id"] = plan.Person.TeamId,
                        ["previous_office_id"] = plan.PreviousOfficeId,
                        ["current_office_id"] = plan.Person.OfficeId
                    }
                };
            }

            if (plan.PromotionDay.HasValue)
            {
                yield return new TemporalEventRecord
                {
                    EventType = "person.promoted",
                    EntityType = "Person",
                    EntityId = plan.Person.Id,
                    RelatedEntityType = "Department",
                    RelatedEntityId = plan.Person.DepartmentId,
                    OccurredAt = startAt.AddDays(plan.PromotionDay.Value).AddMinutes(random.Next(90, 720)),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["previous_title"] = plan.PreviousTitle,
                        ["current_title"] = plan.Person.Title
                    }
                };
            }

            if (plan.TerminationDay.HasValue)
            {
                var reason = TerminationReasons[Math.Abs(plan.Person.Id.GetHashCode()) % TerminationReasons.Length];
                yield return new TemporalEventRecord
                {
                    EventType = "person.terminated",
                    EntityType = "Person",
                    EntityId = plan.Person.Id,
                    RelatedEntityType = "Department",
                    RelatedEntityId = plan.Person.DepartmentId,
                    OccurredAt = startAt.AddDays(plan.TerminationDay.Value).AddMinutes(random.Next(480, 1080)),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["termination_reason"] = reason,
                        ["employee_id"] = plan.Person.EmployeeId,
                        ["user_principal_name"] = plan.Person.UserPrincipalName
                    }
                };
            }
        }
    }

    private static IEnumerable<TemporalEventRecord> BuildAccountEvents(
        SyntheticEnterpriseWorld world,
        IReadOnlyDictionary<string, PersonLifecyclePlan> personPlans,
        DateTimeOffset startAt,
        int totalDays,
        Random random)
    {
        var orderedAccounts = world.Accounts.OrderBy(account => account.Id, StringComparer.OrdinalIgnoreCase).ToList();
        for (var index = 0; index < orderedAccounts.Count; index++)
        {
            var account = orderedAccounts[index];
            personPlans.TryGetValue(account.PersonId ?? string.Empty, out var plan);
            var provisionDay = ClampDay((plan?.HireDay ?? random.Next(0, totalDays + 1)) + random.Next(0, 3), totalDays);

            yield return new TemporalEventRecord
            {
                EventType = "identity.account_provisioned",
                EntityType = "DirectoryAccount",
                EntityId = account.Id,
                RelatedEntityType = "Person",
                RelatedEntityId = account.PersonId,
                OccurredAt = startAt.AddDays(provisionDay).AddMinutes(random.Next(0, 1440)),
                Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_type"] = account.AccountType,
                    ["enabled"] = account.Enabled.ToString().ToLowerInvariant(),
                    ["organizational_unit_id"] = account.OuId,
                    ["identity_provider"] = account.IdentityProvider
                }
            };

            if (account.MfaEnabled && account.AccountType.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                yield return new TemporalEventRecord
                {
                    EventType = "identity.mfa_enrolled",
                    EntityType = "DirectoryAccount",
                    EntityId = account.Id,
                    RelatedEntityType = "Person",
                    RelatedEntityId = account.PersonId,
                    OccurredAt = startAt.AddDays(ClampDay(provisionDay + random.Next(0, 4), totalDays)).AddMinutes(random.Next(0, 1440)),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["identity_provider"] = account.IdentityProvider,
                        ["user_type"] = account.UserType
                    }
                };
            }

            if (ShouldRotatePassword(account, index, provisionDay, totalDays))
            {
                yield return new TemporalEventRecord
                {
                    EventType = "identity.password_rotated",
                    EntityType = "DirectoryAccount",
                    EntityId = account.Id,
                    RelatedEntityType = "Person",
                    RelatedEntityId = account.PersonId,
                    OccurredAt = startAt.AddDays(ClampDay(provisionDay + random.Next(7, Math.Max(8, totalDays / 2) + 1), totalDays)).AddMinutes(random.Next(0, 1440)),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["password_profile"] = account.PasswordProfile,
                        ["password_never_expires"] = account.PasswordNeverExpires.ToString().ToLowerInvariant()
                    }
                };
            }

            if (plan?.TerminationDay is int terminationDay)
            {
                yield return new TemporalEventRecord
                {
                    EventType = "identity.account_disabled",
                    EntityType = "DirectoryAccount",
                    EntityId = account.Id,
                    RelatedEntityType = "Person",
                    RelatedEntityId = account.PersonId,
                    OccurredAt = startAt.AddDays(ClampDay(terminationDay + random.Next(0, 2), totalDays)).AddMinutes(random.Next(0, 1440)),
                    Properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["administrative_tier"] = account.AdministrativeTier,
                        ["disabled_reason"] = "employment_terminated"
                    }
                };
            }
        }
    }

    private static IEnumerable<TemporalEventRecord> BuildDeviceEvents(
        SyntheticEnterpriseWorld world,
        IReadOnlyDictionary<string, PersonLifecyclePlan> personPlans,
        DateTimeOffset startAt,
        int totalDays,
        Random random)
    {
        return world.Devices
            .OrderBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .Select(device =>
            {
                var baselineDay = random.Next(0, totalDays + 1);
                if (personPlans.TryGetValue(device.AssignedPersonId ?? string.Empty, out var plan))
                {
                    baselineDay = ClampDay(plan.HireDay + random.Next(0, 6), totalDays);
                }

                return new TemporalEventRecord
                {
                    EventType = "infrastructure.device_enrolled",
                    EntityType = "ManagedDevice",
                    EntityId = device.Id,
                    RelatedEntityType = "Person",
                    RelatedEntityId = device.AssignedPersonId,
                    OccurredAt = startAt.AddDays(baselineDay).AddMinutes(random.Next(0, 1440)),
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

    private static bool ShouldTransfer(Person person, int index, int totalDays)
        => totalDays >= 5
            && person.PersonType.Equals("Internal", StringComparison.OrdinalIgnoreCase)
            && index > 0
            && index % 5 == 0;

    private static bool ShouldPromote(Person person, int index, int totalDays)
        => totalDays >= 8
            && person.PersonType.Equals("Internal", StringComparison.OrdinalIgnoreCase)
            && !IsLeadershipTitle(person.Title)
            && index > 0
            && index % 6 == 0;

    private static bool ShouldTerminate(Person person, int index, int totalDays)
        => totalDays >= 10
            && person.PersonType.Equals("Internal", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(person.ManagerPersonId)
            && index > 0
            && index % 11 == 0;

    private static bool ShouldRotatePassword(DirectoryAccount account, int index, int provisionDay, int totalDays)
        => totalDays - provisionDay >= 7
            && account.AccountType.Equals("User", StringComparison.OrdinalIgnoreCase)
            && !account.PasswordNeverExpires
            && (index % 4 == 0 || account.Privileged);

    private static string? SelectAlternativeDepartmentId(
        Person person,
        IReadOnlyDictionary<string, List<Department>> departmentsByCompany)
    {
        if (!departmentsByCompany.TryGetValue(person.CompanyId, out var departments))
        {
            return person.DepartmentId;
        }

        return departments
            .Select(department => department.Id)
            .FirstOrDefault(id => !id.Equals(person.DepartmentId, StringComparison.OrdinalIgnoreCase))
            ?? person.DepartmentId;
    }

    private static string? SelectAlternativeTeamId(
        Person person,
        IReadOnlyDictionary<string, List<Team>> teamsByDepartment,
        string? previousDepartmentId)
    {
        if (!string.IsNullOrWhiteSpace(previousDepartmentId)
            && teamsByDepartment.TryGetValue(previousDepartmentId, out var previousDepartmentTeams)
            && previousDepartmentTeams.Count > 0)
        {
            return previousDepartmentTeams[0].Id;
        }

        if (teamsByDepartment.TryGetValue(person.DepartmentId, out var currentDepartmentTeams))
        {
            return currentDepartmentTeams
                .Select(team => team.Id)
                .FirstOrDefault(id => !id.Equals(person.TeamId, StringComparison.OrdinalIgnoreCase))
                ?? person.TeamId;
        }

        return person.TeamId;
    }

    private static string? SelectAlternativeOfficeId(
        Person person,
        IReadOnlyDictionary<string, List<Office>> officesByCompany)
    {
        if (!officesByCompany.TryGetValue(person.CompanyId, out var offices))
        {
            return person.OfficeId;
        }

        return offices
            .Select(office => office.Id)
            .FirstOrDefault(id => !string.Equals(id, person.OfficeId, StringComparison.OrdinalIgnoreCase))
            ?? person.OfficeId;
    }

    private static string InferPreviousTitle(string currentTitle)
    {
        if (currentTitle.StartsWith("Senior ", StringComparison.OrdinalIgnoreCase))
        {
            return currentTitle["Senior ".Length..];
        }

        if (currentTitle.StartsWith("Lead ", StringComparison.OrdinalIgnoreCase))
        {
            return currentTitle["Lead ".Length..];
        }

        if (currentTitle.Contains("Manager", StringComparison.OrdinalIgnoreCase))
        {
            return currentTitle.Replace("Manager", "Specialist", StringComparison.OrdinalIgnoreCase);
        }

        if (currentTitle.Contains("Director", StringComparison.OrdinalIgnoreCase))
        {
            return currentTitle.Replace("Director", "Manager", StringComparison.OrdinalIgnoreCase);
        }

        return $"Associate {currentTitle}";
    }

    private static bool IsLeadershipTitle(string title)
        => title.Contains("Chief", StringComparison.OrdinalIgnoreCase)
            || title.Contains("VP", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Vice President", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Director", StringComparison.OrdinalIgnoreCase);

    private static int ClampDay(int day, int totalDays)
        => Math.Clamp(day, 0, totalDays);

    private sealed record PersonLifecyclePlan(
        Person Person,
        int HireDay,
        int? TransferDay,
        int? PromotionDay,
        int? TerminationDay,
        string? PreviousDepartmentId,
        string? PreviousTeamId,
        string? PreviousOfficeId,
        string? PreviousTitle);
}
