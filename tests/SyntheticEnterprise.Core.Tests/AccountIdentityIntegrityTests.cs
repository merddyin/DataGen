using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Generation.Identity;
using SyntheticEnterprise.Core.Services;

namespace SyntheticEnterprise.Core.Tests;

public sealed class AccountIdentityIntegrityTests
{
    private static readonly string[] PersonBackedAccountTypes =
    [
        "User",
        "Privileged",
        "Contractor",
        "Guest",
        "ManagedServiceProvider"
    ];

    private static readonly string[] NonPersonAccountTypes =
    [
        "BuiltIn",
        "Service",
        "Shared",
        "Device"
    ];

    [Fact]
    public void Accounts_Carry_Person_Names_Only_When_They_Stand_For_A_Person()
    {
        var world = GenerateWorld("Raw Name Fields Co").World;
        var peopleById = world.People.ToDictionary(person => person.Id, StringComparer.OrdinalIgnoreCase);

        Assert.All(NonPersonAccountTypes, accountType =>
            Assert.Contains(world.Accounts, account => account.AccountType == accountType));
        Assert.All(PersonBackedAccountTypes, accountType =>
            Assert.Contains(world.Accounts, account => account.AccountType == accountType));

        foreach (var account in world.Accounts)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(account.Description),
                $"Account {account.Id} ({account.AccountType}) carries no description.");

            if (account.PersonId is null)
            {
                Assert.Null(account.GivenName);
                Assert.Null(account.Surname);
                continue;
            }

            var person = peopleById[account.PersonId];
            Assert.Equal(person.FirstName, account.GivenName);
            Assert.Equal(person.LastName, account.Surname);
        }

        // Non-person classes never gain a given name, whichever person they happen to serve.
        Assert.All(
            world.Accounts.Where(account => NonPersonAccountTypes.Contains(account.AccountType)),
            account =>
            {
                Assert.Null(account.GivenName);
                Assert.Null(account.Surname);
            });
    }

    [Fact]
    public void Account_Descriptions_State_The_Purpose_Of_Each_Account_Class()
    {
        var world = GenerateWorld("Description Purpose Co").World;

        string DescriptionFor(string accountType)
            => world.Accounts.First(account => account.AccountType == accountType).Description!;

        Assert.Contains("Primary user account for", DescriptionFor("User"), StringComparison.Ordinal);
        Assert.Contains("administrative account for", DescriptionFor("Privileged"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Shared mailbox for", DescriptionFor("Shared"), StringComparison.Ordinal);
        Assert.Contains("Built-in account", DescriptionFor("BuiltIn"), StringComparison.Ordinal);
        Assert.Contains("Contractor account for", DescriptionFor("Contractor"), StringComparison.Ordinal);
        Assert.Contains("computer account for", DescriptionFor("Device"), StringComparison.OrdinalIgnoreCase);

        // The Service class holds both the workload accounts and the directory synchronization
        // account the environment defaults create; each states its own purpose.
        var workloadServiceAccount = world.Accounts.First(account =>
            account.AccountType == "Service" && account.DisplayName.StartsWith("svc-", StringComparison.Ordinal));
        Assert.Contains("Service account for the", workloadServiceAccount.Description!, StringComparison.Ordinal);

        var synchronizationAccount = world.Accounts.First(account =>
            account.AccountType == "Service" && account.DisplayName == "Entra Connect Sync");
        Assert.Contains("Directory synchronization service account", synchronizationAccount.Description!, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_World_Issues_No_Duplicate_Account_Upn_Or_Mail_Address()
    {
        // This fact was originally proven on two company names that reduce to one domain slug. That
        // world is no longer generatable: a shared root domain also mints byte-identical
        // distinguished names, which a disambiguating suffix cannot honestly repair, so the scenario
        // is refused. See CompanyPrimaryDomainCollisionTests for the rejection itself.
        //
        // What each assertion below is worth, so nobody reads more coverage into it than it carries:
        // the user principal name assertion is live, and the world-scoped registry does real work for
        // it - a 1200-person company issues 1128 principal names bearing a disambiguating suffix,
        // because duplicate person names inside one company are common. The mail assertions are not
        // independently enforced. Every account derives its mail from its own user principal name,
        // so mail is unique exactly because principal names are, and no generatable world reaches the
        // mail registry's disambiguation branch: measured across a 1200-person single company, this
        // two-company world and a three-company world, zero of 1289, 544 and 1299 mailboxes carry a
        // mail address differing from their own principal name. The registry and its invariant are
        // defence in depth, and WorldInvariantValidatorTests plus the hand-built worlds below are
        // what actually prove they fire.
        var world = GenerateWorld("Aurora Logistics", "Borealis Freight").World;

        Assert.Equal(2, world.Companies.Count);
        Assert.Equal(2, world.Companies.Select(company => company.PrimaryDomain).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var mailAddresses = world.Accounts
            .Select(account => account.Mail)
            .Where(mail => !string.IsNullOrWhiteSpace(mail))
            .ToArray();
        var duplicateMail = mailAddresses
            .GroupBy(mail => mail!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.True(
            duplicateMail.Length == 0,
            $"Accounts share mail addresses: {string.Join(", ", duplicateMail.Take(5))}");

        var upns = world.Accounts
            .Select(account => account.UserPrincipalName)
            .Where(upn => !string.IsNullOrWhiteSpace(upn))
            .ToArray();
        var duplicateUpns = upns
            .GroupBy(upn => upn, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.True(
            duplicateUpns.Length == 0,
            $"Accounts share user principal names: {string.Join(", ", duplicateUpns.Take(5))}");

        // A mail address may equal its own account's user principal name and nothing else.
        var upnOwners = world.Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.UserPrincipalName))
            .ToLookup(account => account.UserPrincipalName, account => account.Id, StringComparer.OrdinalIgnoreCase);
        Assert.All(
            world.Accounts.Where(account => !string.IsNullOrWhiteSpace(account.Mail)),
            account => Assert.All(
                upnOwners[account.Mail!],
                ownerId => Assert.Equal(account.Id, ownerId)));
    }

    [Fact]
    public void Duplicate_Account_Mail_Is_Reported_As_A_Blocking_Metric_And_An_Invariant_Error()
    {
        var world = new SyntheticEnterpriseWorld();
        world.Accounts.AddRange(
        [
            new DirectoryAccount
            {
                Id = "ACT-1",
                CompanyId = "COMP-1",
                AccountType = "User",
                UserPrincipalName = "casey.reed@example.test",
                Mail = "casey.reed@example.test"
            },
            new DirectoryAccount
            {
                Id = "ACT-2",
                CompanyId = "COMP-1",
                AccountType = "Shared",
                UserPrincipalName = "helpdesk@example.test",
                Mail = "casey.reed@example.test"
            }
        ]);

        var audit = new WorldQualityAuditService().Audit(world);
        Assert.True(audit.Metrics["duplicate_account_mail"] > 0);
        Assert.Contains(
            audit.Warnings,
            warning => warning.Contains("duplicate directory account mail", StringComparison.OrdinalIgnoreCase));

        var errors = new WorldInvariantValidator().Validate(world).Errors;
        Assert.Contains(
            errors,
            error => error.Contains("duplicate directory account mail", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Account_Mail_Taken_From_Another_Accounts_Upn_Is_Reported()
    {
        var world = new SyntheticEnterpriseWorld();
        world.Accounts.AddRange(
        [
            new DirectoryAccount
            {
                Id = "ACT-1",
                CompanyId = "COMP-1",
                AccountType = "User",
                UserPrincipalName = "casey.reed@example.test",
                Mail = "casey.reed@example.test"
            },
            new DirectoryAccount
            {
                Id = "ACT-2",
                CompanyId = "COMP-1",
                AccountType = "Shared",
                UserPrincipalName = "helpdesk@example.test",
                Mail = "helpdesk@example.test"
            },
            new DirectoryAccount
            {
                Id = "ACT-3",
                CompanyId = "COMP-1",
                AccountType = "Shared",
                UserPrincipalName = "payroll@example.test",
                Mail = "casey.reed2@example.test"
            }
        ]);

        Assert.Equal(0, new WorldQualityAuditService().Audit(world).Metrics["duplicate_account_mail"]);
        Assert.Empty(new WorldInvariantValidator().Validate(world).Errors);

        world.Accounts[2] = world.Accounts[2] with { Mail = "helpdesk@example.test" };

        Assert.True(new WorldQualityAuditService().Audit(world).Metrics["duplicate_account_mail"] > 0);
        Assert.Contains(
            new WorldInvariantValidator().Validate(world).Errors,
            error => error.Contains("collide with another account's user principal name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sam_Account_Name_Issuance_Never_Returns_A_Value_Already_Issued()
    {
        var issued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A base value that already fills the 20-character limit: truncating the whole candidate
        // would discard the disambiguating suffix and hand back the colliding value.
        var baseValue = new string('a', 19) + "z";
        Assert.Equal(20, baseValue.Length);

        var results = Enumerable
            .Range(0, 500)
            .Select(_ => BasicIdentityGenerator.EnsureUniqueSamAccountName(baseValue, issued))
            .ToArray();

        Assert.Equal(500, results.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(results, value => Assert.True(value.Length <= 20, $"'{value}' exceeds 20 characters."));
        Assert.Equal(500, issued.Count);
    }

    [Fact]
    public void Sam_Account_Name_Issuance_Fails_Loudly_When_Every_Candidate_Is_Taken()
    {
        var issued = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };
        for (var suffix = 2; suffix <= 99999; suffix++)
        {
            issued.Add($"a{suffix}");
        }

        var error = Assert.Throws<InvalidOperationException>(
            () => BasicIdentityGenerator.EnsureUniqueSamAccountName("a", issued));
        Assert.Contains("Unable to issue a unique sAMAccountName", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Privileged_And_External_Accounts_Carry_The_Manager_Their_Person_Reports_To()
    {
        var world = GenerateWorld("Manager Propagation Co").World;
        var peopleById = world.People.ToDictionary(person => person.Id, StringComparer.OrdinalIgnoreCase);
        var primaryAccountByPersonId = world.Accounts
            .Where(account => account.AccountType == "User" && account.PersonId is not null)
            .ToDictionary(account => account.PersonId!, account => account.Id, StringComparer.OrdinalIgnoreCase);

        var secondaryAccounts = world.Accounts
            .Where(account => account.PersonId is not null && account.AccountType != "User")
            .ToArray();
        Assert.Contains(secondaryAccounts, account => account.AccountType == "Privileged");
        Assert.Contains(secondaryAccounts, account => account.AccountType == "Contractor");

        var expectedToCarryManager = secondaryAccounts
            .Where(account => peopleById.TryGetValue(account.PersonId!, out var person)
                              && person.ManagerPersonId is not null
                              && primaryAccountByPersonId.ContainsKey(person.ManagerPersonId))
            .ToArray();
        Assert.NotEmpty(expectedToCarryManager);

        Assert.All(expectedToCarryManager, account =>
        {
            var managerPersonId = peopleById[account.PersonId!].ManagerPersonId!;
            Assert.Equal(primaryAccountByPersonId[managerPersonId], account.ManagerAccountId);
        });
    }

    [Fact]
    public void Sam_Account_Names_Are_Unique_Within_Each_Directory_Domain()
    {
        var world = GenerateWorld("Domain Scoped Alpha", "Domain Scoped Beta").World;

        var collisions = world.Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.SamAccountName))
            .GroupBy(account => $"{account.Domain}|{account.SamAccountName}".ToLowerInvariant())
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.True(collisions.Length == 0, $"sAMAccountName collisions within a domain: {string.Join(", ", collisions.Take(5))}");
        Assert.Equal(0, new WorldQualityAuditService().Audit(world).Metrics["duplicate_account_sam_account_names"]);

        // Separate domains keep their own built-in accounts under their real names.
        var builtInAdministrators = world.Accounts
            .Where(account => account.AccountType == "BuiltIn" && account.DisplayName == "Administrator")
            .ToArray();
        Assert.Equal(2, builtInAdministrators.Length);
        Assert.All(builtInAdministrators, account => Assert.Equal("Administrator", account.SamAccountName));
    }

    [Fact]
    public void Repeated_Generation_Of_The_Same_Scenario_And_Seed_Produces_Identical_Accounts()
    {
        static string Fingerprint(SyntheticEnterpriseWorld world)
            => string.Join(
                "\n",
                world.Accounts.Select(account => string.Join(
                    "|",
                    account.Id,
                    account.AccountType,
                    account.SamAccountName,
                    account.UserPrincipalName,
                    account.Mail,
                    account.GivenName,
                    account.Surname,
                    account.Description,
                    account.ManagerAccountId)));

        var first = GenerateWorld("Determinism Co").World;
        var second = GenerateWorld("Determinism Co").World;

        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    private static GenerationResult GenerateWorld(params string[] companyNames)
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        return generator.Generate(
            new GenerationContext
            {
                Seed = 4242,
                Scenario = new ScenarioDefinition
                {
                    Name = "Account Identity Integrity",
                    Companies = companyNames.Select(BuildCompany).ToList()
                }
            },
            new CatalogSet());
    }

    private static ScenarioCompanyDefinition BuildCompany(string name) => new()
    {
        Name = name,
        Industry = "Manufacturing",
        EmployeeCount = 80,
        BusinessUnitCount = 2,
        DepartmentCountPerBusinessUnit = 2,
        TeamCountPerDepartment = 2,
        OfficeCount = 1,
        SharedMailboxCount = 3,
        ServiceAccountCount = 4,
        IncludePrivilegedAccounts = true,
        Countries = new() { "United States" }
    };
}
