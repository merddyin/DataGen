using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

/// <summary>
/// Covers the configuration items that describe directory accounts. Every assertion is about what
/// was emitted: the person identifier the account object itself holds, the owner text the object
/// carries in its own name attributes, the enabled state, and the columns left empty because the
/// object says nothing that would fill them.
/// </summary>
public sealed class DirectoryAccountConfigurationItemTests
{
    private const string CompanyName = "Account Configuration Item Co";
    private const int ConditionCount = 3;
    private static readonly DateTimeOffset GeneratedAt = new(2026, 5, 11, 8, 15, 0, TimeSpan.Zero);

    [Fact]
    public void Accounts_Are_Not_Described_When_Configuration_Management_Is_Off()
    {
        var world = Generate(includeConfigurationManagement: false);

        Assert.NotEmpty(world.Accounts);
        Assert.Empty(world.ConfigurationItems);
    }

    [Fact]
    public void Every_Account_The_Company_Holds_Is_Described_Exactly_Once()
    {
        var world = Generate();
        var company = world.Companies.Single();
        var accountItems = AccountItems(world);

        Assert.Equal(
            world.Accounts.Count(account => account.CompanyId == company.Id),
            accountItems.Length);
        Assert.Equal(
            accountItems.Length,
            accountItems.Select(item => item.SourceEntityId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            accountItems.Length,
            accountItems.Select(item => item.CiKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(accountItems, item =>
        {
            Assert.Equal(company.Id, item.CompanyId);
            Assert.Equal("Identity", item.CiType);
            Assert.False(string.IsNullOrWhiteSpace(item.CiClass));
            Assert.False(string.IsNullOrWhiteSpace(item.BusinessCriticality));
        });

        var accountsById = AccountsById(world);
        Assert.All(accountItems, item =>
        {
            var account = accountsById[item.SourceEntityId!];
            Assert.Equal(account.SamAccountName, item.Name);
            Assert.Equal(account.UserPrincipalName, item.DisplayName);
            Assert.Equal(account.Description, item.Notes);
            Assert.Equal(account.WhenCreated, item.InstallDate);
        });
    }

    [Fact]
    public void Owner_Columns_Carry_The_Person_Identifier_The_Account_Object_Holds()
    {
        var world = Generate();
        var accountsById = AccountsById(world);
        var accountItems = AccountItems(world);

        Assert.All(accountItems, item =>
        {
            var account = accountsById[item.SourceEntityId!];

            Assert.Equal(account.PersonId, item.BusinessOwnerPersonId);
            if (!string.IsNullOrWhiteSpace(item.BusinessOwnerPersonId))
            {
                Assert.Contains(world.People, person => person.Id == item.BusinessOwnerPersonId);
            }
        });

        Assert.Contains(accountItems, item => !string.IsNullOrWhiteSpace(item.BusinessOwnerPersonId));
    }

    [Fact]
    public void Accounts_With_No_Person_Link_Describe_No_Owner()
    {
        var world = Generate();
        var accountsById = AccountsById(world);
        var unlinkedItems = AccountItems(world)
            .Where(item => string.IsNullOrWhiteSpace(accountsById[item.SourceEntityId!].PersonId))
            .ToArray();

        Assert.NotEmpty(unlinkedItems);
        Assert.All(unlinkedItems, item =>
        {
            Assert.True(string.IsNullOrWhiteSpace(item.BusinessOwnerPersonId));
            Assert.True(string.IsNullOrWhiteSpace(item.OwningDepartmentId));
            Assert.True(string.IsNullOrWhiteSpace(item.OwningLobId));
        });
    }

    [Fact]
    public void No_Account_Is_Described_As_Having_A_Support_Team()
    {
        var world = Generate();

        Assert.All(AccountItems(world), item => Assert.True(string.IsNullOrWhiteSpace(item.SupportTeamId)));
    }

    [Fact]
    public void An_Object_With_No_Owner_Anywhere_On_It_Is_Observed_Without_One()
    {
        var world = Generate();
        var accountsById = AccountsById(world);
        var ownerlessItems = AccountItems(world)
            .Where(item =>
            {
                var account = accountsById[item.SourceEntityId!];
                return string.IsNullOrWhiteSpace(account.PersonId)
                       && string.IsNullOrWhiteSpace(account.GivenName)
                       && string.IsNullOrWhiteSpace(account.Surname);
            })
            .ToArray();

        Assert.NotEmpty(ownerlessItems);
        Assert.All(ownerlessItems, item =>
        {
            Assert.True(string.IsNullOrWhiteSpace(item.BusinessOwnerPersonId));
            Assert.True(string.IsNullOrWhiteSpace(item.TechnicalOwnerPersonId));

            foreach (var record in SourceRecordsFor(world, item))
            {
                Assert.True(string.IsNullOrWhiteSpace(record.ObservedBusinessOwner));
                Assert.True(string.IsNullOrWhiteSpace(record.ObservedTechnicalOwner));
                Assert.True(string.IsNullOrWhiteSpace(record.ObservedSupportGroup));
            }
        });
    }

    [Fact]
    public void An_Object_Carrying_Name_Attributes_Is_Observed_By_The_Name_It_Carries()
    {
        var world = Generate();
        var accountsById = AccountsById(world);
        var namedItems = AccountItems(world)
            .Where(item =>
            {
                var account = accountsById[item.SourceEntityId!];
                return !string.IsNullOrWhiteSpace(account.GivenName) && !string.IsNullOrWhiteSpace(account.Surname);
            })
            .ToArray();

        Assert.NotEmpty(namedItems);
        Assert.All(namedItems, item =>
        {
            var account = accountsById[item.SourceEntityId!];
            var carriedName = $"{account.GivenName} {account.Surname}";

            foreach (var record in SourceRecordsFor(world, item))
            {
                Assert.Equal(carriedName, record.ObservedBusinessOwner);
            }
        });
    }

    [Fact]
    public void An_Object_Naming_Someone_Other_Than_Its_Linked_Person_Keeps_Both_Facts()
    {
        var world = Generate();
        var accountsById = AccountsById(world);
        var peopleById = world.People.ToDictionary(person => person.Id, StringComparer.OrdinalIgnoreCase);
        var disagreeingItems = AccountItems(world)
            .Where(item =>
            {
                var account = accountsById[item.SourceEntityId!];
                return !string.IsNullOrWhiteSpace(account.PersonId)
                       && !string.IsNullOrWhiteSpace(account.GivenName)
                       && !string.IsNullOrWhiteSpace(account.Surname)
                       && peopleById.TryGetValue(account.PersonId!, out var person)
                       && !string.Equals(person.FirstName, account.GivenName, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        Assert.NotEmpty(disagreeingItems);
        Assert.All(disagreeingItems, item =>
        {
            var account = accountsById[item.SourceEntityId!];
            var linkedPerson = peopleById[account.PersonId!];

            // The item carries the identifier of the person the object is linked to.
            Assert.Equal(account.PersonId, item.BusinessOwnerPersonId);

            foreach (var record in SourceRecordsFor(world, item))
            {
                // The collected row carries the name the object itself holds, which is somebody else.
                Assert.Equal($"{account.GivenName} {account.Surname}", record.ObservedBusinessOwner);
                Assert.NotEqual(linkedPerson.DisplayName, record.ObservedBusinessOwner);
            }
        });
    }

    [Fact]
    public void An_Object_Whose_Only_Owner_Record_Is_Prose_Keeps_The_Prose_And_Claims_No_Owner()
    {
        var world = Generate();
        var company = world.Companies.Single();
        var accountsById = AccountsById(world);
        var proseOwnedItems = AccountItems(world)
            .Where(item =>
            {
                var account = accountsById[item.SourceEntityId!];
                return string.IsNullOrWhiteSpace(account.PersonId)
                       && !string.IsNullOrWhiteSpace(account.Description)
                       && world.People.Any(person => person.CompanyId == company.Id
                                                     && account.Description!.Contains(person.DisplayName, StringComparison.Ordinal));
            })
            .ToArray();

        Assert.NotEmpty(proseOwnedItems);
        Assert.All(proseOwnedItems, item =>
        {
            var account = accountsById[item.SourceEntityId!];

            Assert.Equal(account.Description, item.Notes);
            Assert.True(string.IsNullOrWhiteSpace(item.BusinessOwnerPersonId));
            Assert.True(string.IsNullOrWhiteSpace(item.TechnicalOwnerPersonId));
            Assert.True(string.IsNullOrWhiteSpace(item.SupportTeamId));
            Assert.True(string.IsNullOrWhiteSpace(item.OwningDepartmentId));
        });
    }

    [Fact]
    public void A_Disabled_Object_Is_Described_And_Observed_As_Disabled()
    {
        var world = Generate();
        var accountsById = AccountsById(world);
        var accountItems = AccountItems(world);
        var disabledItems = accountItems
            .Where(item => !accountsById[item.SourceEntityId!].Enabled)
            .ToArray();

        Assert.NotEmpty(disabledItems);
        Assert.All(disabledItems, item =>
        {
            Assert.Equal("Disabled", item.OperationalStatus);
            Assert.Equal("Retired", item.LifecycleStatus);

            foreach (var record in SourceRecordsFor(world, item))
            {
                Assert.Equal("Disabled", record.ObservedOperationalStatus);
                Assert.Equal("Retired", record.ObservedLifecycleStatus);
            }
        });

        Assert.All(
            accountItems.Where(item => accountsById[item.SourceEntityId!].Enabled),
            item =>
            {
                Assert.Equal("Active", item.OperationalStatus);
                Assert.Equal("InService", item.LifecycleStatus);
            });
    }

    [Fact]
    public void An_Invited_Object_Names_The_Person_Behind_The_Account_That_Sponsored_It()
    {
        var world = Generate(includeExternalWorkforce: true);
        var accountsById = AccountsById(world);
        var sponsoredItems = AccountItems(world)
            .Where(item => !string.IsNullOrWhiteSpace(accountsById[item.SourceEntityId!].InvitedByAccountId))
            .ToArray();

        Assert.NotEmpty(sponsoredItems);
        Assert.All(sponsoredItems, item =>
        {
            var account = accountsById[item.SourceEntityId!];
            var sponsor = accountsById[account.InvitedByAccountId!];

            Assert.Equal(sponsor.PersonId, item.TechnicalOwnerPersonId);
        });

        Assert.All(
            AccountItems(world).Where(item => string.IsNullOrWhiteSpace(accountsById[item.SourceEntityId!].InvitedByAccountId)),
            item => Assert.True(string.IsNullOrWhiteSpace(item.TechnicalOwnerPersonId)));
    }

    [Fact]
    public void No_Relationship_Starts_Or_Ends_At_An_Account_Configuration_Item()
    {
        var world = Generate();
        var accountItemIds = AccountItems(world).Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(accountItemIds);
        Assert.DoesNotContain(
            world.ConfigurationItemRelationships,
            relationship => accountItemIds.Contains(relationship.SourceConfigurationItemId)
                            || accountItemIds.Contains(relationship.TargetConfigurationItemId));
    }

    [Fact]
    public void Observed_Owner_Text_Never_Carries_A_Person_Identifier()
    {
        var world = Generate(deviationProfile: ScenarioDeviationProfiles.Aggressive);
        var accountItemIds = AccountItems(world).Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var personIds = world.People.Select(person => person.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accountRecords = world.CmdbSourceLinks
            .Where(link => accountItemIds.Contains(link.ConfigurationItemId))
            .Join(world.CmdbSourceRecords, link => link.SourceRecordId, record => record.Id, (_, record) => record)
            .ToArray();

        var observedOwnerText = accountRecords
            .SelectMany(record => new[] { record.ObservedBusinessOwner, record.ObservedTechnicalOwner })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        Assert.NotEmpty(accountRecords);
        Assert.NotEmpty(observedOwnerText);
        Assert.All(observedOwnerText, text => Assert.DoesNotContain(text, personIds));
    }

    [Fact]
    public void Account_Items_Are_Identical_Across_Runs_Of_The_Same_Scenario_Seed_And_Time()
    {
        Assert.Equal(Fingerprint(Generate()), Fingerprint(Generate()));
    }

    private static string Fingerprint(SyntheticEnterpriseWorld world)
        => string.Join(
            Environment.NewLine,
            AccountItems(world).Select(item => string.Join(
                '|',
                item.Id,
                item.CiKey,
                item.Name,
                item.DisplayName,
                item.CiClass,
                item.SourceEntityId,
                item.OperationalStatus,
                item.LifecycleStatus,
                item.BusinessOwnerPersonId,
                item.TechnicalOwnerPersonId,
                item.SupportTeamId,
                item.OwningDepartmentId,
                item.OwningLobId,
                item.ServiceTier,
                item.ServiceClassification,
                item.BusinessCriticality,
                item.InstallDate?.ToString("O", CultureInfo.InvariantCulture),
                item.LastReviewedAt?.ToString("O", CultureInfo.InvariantCulture),
                item.Notes)));

    private static ConfigurationItem[] AccountItems(SyntheticEnterpriseWorld world)
        => world.ConfigurationItems
            .Where(item => string.Equals(item.SourceEntityType, "DirectoryAccount", StringComparison.Ordinal))
            .ToArray();

    private static Dictionary<string, DirectoryAccount> AccountsById(SyntheticEnterpriseWorld world)
        => world.Accounts.ToDictionary(account => account.Id, StringComparer.OrdinalIgnoreCase);

    private static CmdbSourceRecord[] SourceRecordsFor(SyntheticEnterpriseWorld world, ConfigurationItem item)
    {
        var recordIds = world.CmdbSourceLinks
            .Where(link => string.Equals(link.ConfigurationItemId, item.Id, StringComparison.Ordinal))
            .Select(link => link.SourceRecordId)
            .ToHashSet(StringComparer.Ordinal);

        return world.CmdbSourceRecords.Where(record => recordIds.Contains(record.Id)).ToArray();
    }

    private static SyntheticEnterpriseWorld Generate(
        bool includeConfigurationManagement = true,
        bool includeExternalWorkforce = false,
        string deviationProfile = ScenarioDeviationProfiles.Clean)
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();

        return generator.Generate(
            new GenerationContext
            {
                Seed = 8814,
                GeneratedAt = GeneratedAt,
                Scenario = new ScenarioDefinition
                {
                    Name = "Account Configuration Item Test",
                    DeviationProfile = deviationProfile,
                    Identity = new IdentityProfile
                    {
                        AccountOwnershipConditionCount = ConditionCount,
                        IncludeExternalWorkforce = includeExternalWorkforce,
                        IncludeB2BGuests = includeExternalWorkforce
                    },
                    Cmdb = new CmdbProfile
                    {
                        IncludeConfigurationManagement = includeConfigurationManagement,
                        IncludeAutoDiscoveryRecords = true,
                        IncludeServiceCatalogRecords = true,
                        IncludeSpreadsheetImportRecords = true
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
            },
            new CatalogSet()).World;
    }
}
