using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Scenarios;
using SyntheticEnterprise.Core.Services;

namespace SyntheticEnterprise.Core.Tests;

/// <summary>
/// Covers the directory account ownership conditions the identity profile can be asked for. Every
/// assertion is about what the directory emitted — the employee link, the name attributes, the
/// enabled state, the timestamps, the description and the group memberships — and each condition is
/// located in the world the way a reader of the records would locate it, never by knowing which
/// method produced it.
/// </summary>
public sealed class DirectoryAccountOwnershipConditionTests
{
    private const int ConditionCount = 2;
    private const string CompanyName = "Ownership Condition Co";
    private static readonly DateTimeOffset GeneratedAt = new(2026, 4, 17, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Option_Is_Off_By_Default()
    {
        var world = Generate(conditionCount: 0);

        Assert.Equal(0, new IdentityProfile().AccountOwnershipConditionCount);
        Assert.DoesNotContain(world.Accounts, account => account.AccountType == "Secondary");
        Assert.DoesNotContain(
            world.Accounts,
            account => string.IsNullOrWhiteSpace(account.PersonId) && !string.IsNullOrWhiteSpace(account.GivenName));
    }

    [Fact]
    public void Held_Secondary_Accounts_Carry_Their_Holders_Names_And_Their_Own_Identifiers()
    {
        var world = Generate();
        var heldSecondaries = world.Accounts
            .Where(account => account.AccountType == "Secondary" && !string.IsNullOrWhiteSpace(account.PersonId))
            .ToArray();

        Assert.Equal(ConditionCount, heldSecondaries.Length);
        Assert.All(heldSecondaries, account =>
        {
            var holder = Assert.Single(world.People, person => person.Id == account.PersonId);
            var primary = PrimaryAccountOf(world, holder);

            Assert.Equal(holder.FirstName, account.GivenName);
            Assert.Equal(holder.LastName, account.Surname);
            Assert.NotEqual(primary.Id, account.Id);
            Assert.NotEqual(primary.UserPrincipalName, account.UserPrincipalName);
            Assert.NotEqual(primary.SamAccountName, account.SamAccountName);
            Assert.NotEqual(primary.DistinguishedName, account.DistinguishedName);
            Assert.True(account.Enabled);
            Assert.Contains(holder.DisplayName, account.Description!, StringComparison.Ordinal);
            Assert.Contains(primary.SamAccountName, account.Description!, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Accounts_Retained_Through_A_Holder_Change_Name_Someone_Other_Than_Their_Link()
    {
        var world = Generate();
        var retained = RetainedThroughHolderChangeAccounts(world);

        Assert.Equal(ConditionCount, retained.Length);
        Assert.All(retained, account =>
        {
            var holder = Assert.Single(world.People, person => person.Id == account.PersonId);

            Assert.NotEqual(holder.FirstName, account.GivenName);
            Assert.NotEqual(holder.LastName, account.Surname);
            Assert.Equal($"{account.GivenName} {account.Surname}", account.DisplayName);

            // The name attributes belong to a holder the directory keeps no person record for.
            Assert.DoesNotContain(world.People, person => person.DisplayName == account.DisplayName);

            // The login name and the mail address were minted from those name attributes too.
            Assert.StartsWith(
                $"{account.GivenName!.ToLowerInvariant()}.{account.Surname!.ToLowerInvariant()}@",
                account.UserPrincipalName,
                StringComparison.Ordinal);
            Assert.Equal(account.UserPrincipalName, account.Mail);
            Assert.Null(account.EmployeeId);
            Assert.Contains(account.DisplayName, account.Description!, StringComparison.Ordinal);

            // The holder's own primary account is untouched and still links to them.
            Assert.Equal(holder.Id, PrimaryAccountOf(world, holder).PersonId);
        });
    }

    [Fact]
    public void A_Second_Object_Presents_As_The_Primary_For_One_Holder()
    {
        var world = Generate();
        var secondPrimaries = SecondPrimaryObjectAccounts(world);

        Assert.Equal(ConditionCount, secondPrimaries.Length);
        Assert.All(secondPrimaries, account =>
        {
            var holder = Assert.Single(world.People, person => person.Id == account.PersonId);
            var primary = PrimaryAccountOf(world, holder);

            // Nothing on either object says which of the two is the primary: same type, same
            // names, same employee number and a description in the same shape.
            Assert.Equal("User", account.AccountType);
            Assert.Equal(primary.AccountType, account.AccountType);
            Assert.Equal(holder.DisplayName, account.DisplayName);
            Assert.Equal(holder.FirstName, account.GivenName);
            Assert.Equal(holder.LastName, account.Surname);
            Assert.Equal(holder.EmployeeId, account.EmployeeId);
            Assert.StartsWith("Primary user account for ", account.Description!, StringComparison.Ordinal);
            Assert.True(account.Enabled);

            // The identifiers are still the holder's own, disambiguated the way a directory does.
            Assert.NotEqual(primary.UserPrincipalName, account.UserPrincipalName);
            Assert.NotEqual(primary.SamAccountName, account.SamAccountName);
            Assert.NotEqual(primary.Mail, account.Mail);
            Assert.NotEqual(primary.DistinguishedName, account.DistinguishedName);
            Assert.Equal(holder.Id, primary.PersonId);
        });
    }

    [Fact]
    public void Disabled_Accounts_Carry_A_Disabled_State_And_The_Timestamps_That_Go_With_It()
    {
        var world = Generate();
        var disabled = world.Accounts
            .Where(account => !account.Enabled
                              && account.AccountType == "User"
                              && !string.IsNullOrWhiteSpace(account.PersonId))
            .ToArray();

        Assert.Equal(ConditionCount, disabled.Length);
        Assert.All(disabled, account =>
        {
            var holder = Assert.Single(world.People, person => person.Id == account.PersonId);

            Assert.False(account.Enabled);
            Assert.Equal(holder.DisplayName, account.DisplayName);
            Assert.Equal(holder.FirstName, account.GivenName);
            Assert.Equal(holder.LastName, account.Surname);

            // Created, then used, then last modified on the day it was disabled.
            Assert.NotNull(account.WhenCreated);
            Assert.NotNull(account.LastLogon);
            Assert.NotNull(account.WhenModified);
            Assert.True(account.WhenCreated < account.LastLogon);
            Assert.True(account.LastLogon < account.WhenModified);
            Assert.True(account.WhenModified <= GeneratedAt);

            // The password expired before the object was disabled, and nothing renewed it.
            Assert.NotNull(account.PasswordExpires);
            Assert.True(account.PasswordExpires < account.WhenModified);
            Assert.False(account.PasswordNeverExpires);

            Assert.Contains(
                account.WhenModified!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                account.Description!,
                StringComparison.Ordinal);

            // The holder's live primary account is unaffected.
            Assert.True(PrimaryAccountOf(world, holder).Enabled);
        });
    }

    [Fact]
    public void Accounts_With_No_Employee_Link_Still_Carry_Real_Name_Attributes()
    {
        var world = Generate();
        var unlinked = world.Accounts
            .Where(account => string.IsNullOrWhiteSpace(account.PersonId)
                              && !string.IsNullOrWhiteSpace(account.GivenName))
            .ToArray();

        Assert.Equal(ConditionCount, unlinked.Length);
        Assert.All(unlinked, account =>
        {
            Assert.Null(account.PersonId);
            Assert.Null(account.EmployeeId);
            Assert.Null(account.ManagerAccountId);
            Assert.False(string.IsNullOrWhiteSpace(account.GivenName));
            Assert.False(string.IsNullOrWhiteSpace(account.Surname));
            Assert.Contains(account.GivenName!, account.DisplayName, StringComparison.Ordinal);
            Assert.Contains(account.Surname!, account.DisplayName, StringComparison.Ordinal);

            // The names are a real person's, and that person keeps their own primary account and
            // its link; this object claims nothing about who holds it.
            var namesakes = world.People
                .Where(person => person.FirstName == account.GivenName && person.LastName == account.Surname)
                .ToArray();
            Assert.NotEmpty(namesakes);
            Assert.All(namesakes, namesake => Assert.Equal(namesake.Id, PrimaryAccountOf(world, namesake).PersonId));
        });
    }

    [Fact]
    public void Accounts_With_No_Owner_Record_Name_No_Owner_Anywhere()
    {
        var world = Generate();
        var unowned = UnownedAccounts(world);

        Assert.Equal(ConditionCount, unowned.Length);
        Assert.All(unowned, account =>
        {
            Assert.Null(account.PersonId);
            Assert.Null(account.GivenName);
            Assert.Null(account.Surname);
            Assert.Null(account.EmployeeId);
            Assert.Null(account.ManagerAccountId);
            Assert.Null(account.Mail);
            Assert.False(string.IsNullOrWhiteSpace(account.Description));

            // No person and no team is named in the only free-text field the object has.
            Assert.DoesNotContain(
                world.People,
                person => account.Description!.Contains(person.DisplayName, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                world.Departments,
                department => account.Description!.Contains(department.Name, StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Accounts_Owned_In_Prose_Name_A_Team_And_A_Contact_And_No_Identifier()
    {
        var world = Generate();
        var teamOwned = TeamOwnedAccounts(world);

        Assert.Equal(ConditionCount, teamOwned.Length);
        Assert.All(teamOwned, account =>
        {
            Assert.Null(account.PersonId);
            Assert.Null(account.GivenName);
            Assert.Null(account.Surname);
            Assert.Null(account.EmployeeId);
            Assert.Null(account.ManagerAccountId);

            var departments = world.Departments
                .Where(candidate => account.Description!.Contains(candidate.Name, StringComparison.Ordinal))
                .ToArray();
            var contact = Assert.Single(
                world.People,
                person => account.Description!.Contains(person.DisplayName, StringComparison.Ordinal));

            // The contact works in the team the description names, and neither is emitted as an
            // identifier anywhere on the object.
            Assert.NotEmpty(departments);
            Assert.Contains(departments, department => department.Id == contact.DepartmentId);
            Assert.DoesNotContain(contact.Id, account.Description!, StringComparison.Ordinal);
            Assert.All(departments, department =>
                Assert.DoesNotContain(department.Id, account.Description!, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Shared_Mailbox_Access_Is_Held_By_Several_Named_People()
    {
        var world = Generate();
        var shared = world.Accounts
            .Where(account => account.AccountType == "Shared")
            .ToArray();
        var mailboxesUsedByNamedPeople = shared
            .Select(mailbox => new
            {
                Mailbox = mailbox,
                Holders = DirectAccessHolders(world, mailbox)
            })
            .Where(entry => entry.Holders.Length > 0)
            .ToArray();

        Assert.Equal(ConditionCount, mailboxesUsedByNamedPeople.Length);
        Assert.All(mailboxesUsedByNamedPeople, entry =>
        {
            Assert.True(entry.Holders.Length > 1);
            Assert.All(entry.Holders, holderAccount =>
            {
                Assert.Equal("User", holderAccount.AccountType);
                Assert.Contains(world.People, person => person.Id == holderAccount.PersonId);
            });

            // The mailbox object itself records that its access is held by named people.
            Assert.Contains(
                entry.Holders.Length.ToString(CultureInfo.InvariantCulture),
                entry.Mailbox.Description!);
            Assert.Contains("named people", entry.Mailbox.Description!, StringComparison.Ordinal);

            // It is still the mailbox the company already had, not a replacement for it.
            Assert.Null(entry.Mailbox.PersonId);
            Assert.StartsWith("Shared mailbox for ", entry.Mailbox.Description!, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Every_Account_The_Joiner_Process_Created_Is_Left_As_It_Was()
    {
        var baseline = Generate(conditionCount: 0);
        var world = Generate();
        var emitted = world.Accounts.ToDictionary(account => account.Id, StringComparer.Ordinal);

        // Machine accounts are minted after the identity layer, so their identifiers move when
        // anything before them changes; every directory account the identity layer issues is
        // compared exactly as it was.
        var directoryAccounts = baseline.Accounts
            .Where(account => account.AccountType != "Device")
            .ToArray();

        Assert.NotEmpty(directoryAccounts);
        Assert.All(directoryAccounts, account =>
        {
            var current = Assert.Contains(account.Id, emitted);

            Assert.Equal(account.PersonId, current.PersonId);
            Assert.Equal(account.AccountType, current.AccountType);
            Assert.Equal(account.DisplayName, current.DisplayName);
            Assert.Equal(account.GivenName, current.GivenName);
            Assert.Equal(account.Surname, current.Surname);
            Assert.Equal(account.SamAccountName, current.SamAccountName);
            Assert.Equal(account.UserPrincipalName, current.UserPrincipalName);
            Assert.Equal(account.Mail, current.Mail);
            Assert.Equal(account.DistinguishedName, current.DistinguishedName);
            Assert.Equal(account.EmployeeId, current.EmployeeId);
            Assert.Equal(account.Enabled, current.Enabled);
            Assert.Equal(account.WhenCreated, current.WhenCreated);
            Assert.Equal(account.WhenModified, current.WhenModified);

            // Only a shared mailbox gains text, and only by keeping what it already said.
            if (account.AccountType == "Shared")
            {
                Assert.StartsWith(account.Description!, current.Description!, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(account.Description, current.Description);
            }
        });
    }

    [Fact]
    public void Every_Person_Keeps_Exactly_One_Account_Carrying_Their_Own_User_Principal_Name()
    {
        var world = Generate();

        Assert.All(world.People.Where(person => !string.IsNullOrWhiteSpace(person.UserPrincipalName)), person =>
        {
            var account = Assert.Single(
                world.Accounts,
                candidate => candidate.UserPrincipalName == person.UserPrincipalName);
            Assert.Equal(person.Id, account.PersonId);
        });
    }

    [Fact]
    public void Identifiers_Stay_Unique_Across_Every_Emitted_Account()
    {
        var world = Generate();

        Assert.Empty(DuplicateValues(world.Accounts.Select(account => account.UserPrincipalName)));
        Assert.Empty(DuplicateValues(world.Accounts.Select(account => account.Mail)));
        Assert.Empty(DuplicateValues(world.Accounts.Select(account => account.DistinguishedName)));
        Assert.Empty(DuplicateValues(world.Accounts.Select(account => account.SamAccountName)));

        var upns = world.Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.UserPrincipalName))
            .ToDictionary(account => account.UserPrincipalName, account => account.Id, StringComparer.OrdinalIgnoreCase);
        Assert.All(world.Accounts.Where(account => !string.IsNullOrWhiteSpace(account.Mail)), account =>
        {
            if (upns.TryGetValue(account.Mail!, out var owner))
            {
                Assert.Equal(account.Id, owner);
            }
        });
    }

    [Fact]
    public void The_Emitted_Conditions_Breach_No_World_Quality_Gate()
    {
        var baseline = WorldQualityValidationService.EvaluateScenario(
            "account-ownership-conditions-off",
            seed: 4127,
            GenerateResult(conditionCount: 0).Quality);
        var emitted = WorldQualityValidationService.EvaluateScenario(
            "account-ownership-conditions-on",
            seed: 4127,
            GenerateResult().Quality);

        Assert.Empty(baseline.BlockingMetrics);
        Assert.Empty(emitted.BlockingMetrics);
        Assert.NotEqual("fail", baseline.Status);
        Assert.NotEqual("fail", emitted.Status);
    }

    [Fact]
    public void Two_Runs_Of_The_Same_Scenario_Emit_The_Same_Accounts()
    {
        var first = Generate();
        var second = Generate();

        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    [Fact]
    public void The_Option_Declares_The_Version_That_Introduced_It()
    {
        Assert.True(ScenarioOptionLifecycleRegistry.Default.TryGetIntroduction(
            "$.identity.accountOwnershipConditionCount",
            out var introduction));
        Assert.Equal("0.13.0", introduction.IntroducedInVersion);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(IdentityProfile.MaximumAccountOwnershipConditionCount + 1)]
    public void Validator_Rejects_A_Count_Outside_The_Bounds(int count)
    {
        var result = Validate(count, employeeCount: 400, sharedMailboxCount: 40);

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, message => message.Code == "identity-account-ownership-condition-count");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(IdentityProfile.MaximumAccountOwnershipConditionCount)]
    public void Validator_Accepts_A_Count_On_The_Bounds(int count)
    {
        var result = Validate(
            count,
            employeeCount: IdentityProfile.MaximumAccountOwnershipConditionCount * 5,
            sharedMailboxCount: IdentityProfile.MaximumAccountOwnershipConditionCount);

        Assert.DoesNotContain(result.Messages, message => message.Code == "identity-account-ownership-condition-count");
        Assert.DoesNotContain(result.Messages, message => message.Code == "identity-account-ownership-condition-population");
    }

    [Fact]
    public void Validator_Rejects_A_Count_The_Largest_Company_Cannot_Supply_People_For()
    {
        var result = Validate(count: 5, employeeCount: 20, sharedMailboxCount: 10);

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, message => message.Code == "identity-account-ownership-condition-population");
    }

    [Fact]
    public void Validator_Rejects_A_Count_Without_A_Shared_Mailbox_To_Attach_Access_To()
    {
        var result = Validate(count: 2, employeeCount: 400, sharedMailboxCount: 1);

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, message => message.Code == "identity-account-ownership-condition-population");
    }

    private static DirectoryAccount[] RetainedThroughHolderChangeAccounts(SyntheticEnterpriseWorld world)
    {
        var peopleById = world.People.ToDictionary(person => person.Id, StringComparer.Ordinal);

        return world.Accounts
            .Where(account => account.AccountType == "User"
                              && account.Enabled
                              && !string.IsNullOrWhiteSpace(account.PersonId)
                              && peopleById.TryGetValue(account.PersonId!, out var holder)
                              && account.GivenName != holder.FirstName)
            .ToArray();
    }

    private static DirectoryAccount[] SecondPrimaryObjectAccounts(SyntheticEnterpriseWorld world)
        => world.Accounts
            .Where(account => account.AccountType == "User" && account.Enabled && !string.IsNullOrWhiteSpace(account.PersonId))
            .GroupBy(account => account.PersonId!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group
                .Where(account => world.People.Any(person => person.Id == group.Key
                                                             && account.GivenName == person.FirstName
                                                             && account.DisplayName == person.DisplayName))
                .Skip(1))
            .ToArray();

    private static DirectoryAccount[] UnownedAccounts(SyntheticEnterpriseWorld world)
        => world.Accounts
            .Where(account => account.AccountType == "Service"
                              && string.IsNullOrWhiteSpace(account.PersonId)
                              && string.IsNullOrWhiteSpace(account.GivenName)
                              && !world.People.Any(person =>
                                  (account.Description ?? string.Empty).Contains(person.DisplayName, StringComparison.OrdinalIgnoreCase))
                              && !world.Departments.Any(department =>
                                  (account.Description ?? string.Empty).Contains(department.Name, StringComparison.OrdinalIgnoreCase))
                              && (account.Description ?? string.Empty).Contains("No owner is recorded", StringComparison.Ordinal))
            .ToArray();

    private static DirectoryAccount[] TeamOwnedAccounts(SyntheticEnterpriseWorld world)
        => world.Accounts
            .Where(account => string.IsNullOrWhiteSpace(account.PersonId)
                              && string.IsNullOrWhiteSpace(account.GivenName)
                              && world.Departments.Any(department =>
                                  (account.Description ?? string.Empty).Contains(department.Name, StringComparison.Ordinal))
                              && world.People.Any(person =>
                                  (account.Description ?? string.Empty).Contains(person.DisplayName, StringComparison.Ordinal)))
            .ToArray();

    private static DirectoryAccount[] DirectAccessHolders(SyntheticEnterpriseWorld world, DirectoryAccount mailbox)
    {
        var accessGroup = world.Groups.FirstOrDefault(group =>
            group.CompanyId == mailbox.CompanyId
            && group.Name == $"ACL MBX {mailbox.DisplayName} Access");
        if (accessGroup is null)
        {
            return Array.Empty<DirectoryAccount>();
        }

        var memberIds = world.GroupMemberships
            .Where(membership => membership.GroupId == accessGroup.Id && membership.MemberObjectType == "Account")
            .Select(membership => membership.MemberObjectId)
            .ToHashSet(StringComparer.Ordinal);

        return world.Accounts.Where(account => memberIds.Contains(account.Id)).ToArray();
    }

    private static DirectoryAccount PrimaryAccountOf(SyntheticEnterpriseWorld world, Person person)
        => Assert.Single(world.Accounts, account => account.UserPrincipalName == person.UserPrincipalName);

    private static string[] DuplicateValues(IEnumerable<string?> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

    private static string Fingerprint(SyntheticEnterpriseWorld world)
        => string.Join(
            Environment.NewLine,
            world.Accounts.Select(account => string.Join(
                '',
                account.Id,
                account.PersonId,
                account.AccountType,
                account.DisplayName,
                account.GivenName,
                account.Surname,
                account.Description,
                account.SamAccountName,
                account.UserPrincipalName,
                account.Mail,
                account.DistinguishedName,
                account.EmployeeId,
                account.ManagerAccountId,
                account.Enabled.ToString(),
                account.WhenCreated?.ToString("O", CultureInfo.InvariantCulture),
                account.WhenModified?.ToString("O", CultureInfo.InvariantCulture),
                account.LastLogon?.ToString("O", CultureInfo.InvariantCulture),
                account.PasswordExpires?.ToString("O", CultureInfo.InvariantCulture))));

    private static ScenarioValidationResult Validate(int count, int employeeCount, int sharedMailboxCount)
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var validator = services.GetRequiredService<IScenarioValidator>();

        return validator.Validate(new ScenarioEnvelope
        {
            Name = "Account Ownership Condition Count",
            Identity = new IdentityProfile { AccountOwnershipConditionCount = count },
            Companies = new()
            {
                new ScenarioCompanyDefinition
                {
                    Name = CompanyName,
                    EmployeeCount = employeeCount,
                    SharedMailboxCount = sharedMailboxCount,
                    Countries = new() { "United States" }
                }
            }
        });
    }

    private static SyntheticEnterpriseWorld Generate(int conditionCount = ConditionCount)
        => GenerateResult(conditionCount).World;

    private static GenerationResult GenerateResult(int conditionCount = ConditionCount)
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var context = new GenerationContext
        {
            Seed = 4127,
            GeneratedAt = GeneratedAt,
            Scenario = new ScenarioDefinition
            {
                Name = "Account Ownership Condition Test",
                Identity = new IdentityProfile
                {
                    AccountOwnershipConditionCount = conditionCount,
                    IncludeExternalWorkforce = false,
                    IncludeB2BGuests = false
                },
                Companies = new()
                {
                    new ScenarioCompanyDefinition
                    {
                        Name = CompanyName,
                        Industry = "Manufacturing",
                        EmployeeCount = 140,
                        BusinessUnitCount = 2,
                        DepartmentCountPerBusinessUnit = 2,
                        TeamCountPerDepartment = 2,
                        OfficeCount = 2,
                        SharedMailboxCount = 5,
                        ServiceAccountCount = 4,
                        IncludePrivilegedAccounts = true,
                        Countries = new() { "United States" }
                    }
                }
            }
        };

        return generator.Generate(context, new CatalogSet());
    }
}
