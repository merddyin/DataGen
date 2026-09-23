using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Services;

namespace SyntheticEnterprise.Core.Tests;

public sealed class WorldInvariantValidatorTests
{
    [Fact]
    public void Validate_Flags_Duplicate_User_Principal_Names_As_Errors()
    {
        var validator = new WorldInvariantValidator();
        var world = new SyntheticEnterpriseWorld();
        world.People.AddRange(
        [
            new Person
            {
                Id = "P-1",
                CompanyId = "COMP-1",
                FirstName = "Della",
                LastName = "Duck",
                DisplayName = "Della Duck",
                UserPrincipalName = "della@duckburg.test"
            },
            new Person
            {
                Id = "P-2",
                CompanyId = "COMP-1",
                FirstName = "Donald",
                LastName = "Duck",
                DisplayName = "Donald Duck",
                UserPrincipalName = "della@duckburg.test"
            }
        ]);
        world.Accounts.AddRange(
        [
            new DirectoryAccount
            {
                Id = "A-1",
                CompanyId = "COMP-1",
                UserPrincipalName = "donald@duckburg.test"
            },
            new DirectoryAccount
            {
                Id = "A-2",
                CompanyId = "COMP-1",
                UserPrincipalName = "donald@duckburg.test"
            }
        ]);

        var result = validator.Validate(world);

        Assert.Contains(result.Errors, error => error.Contains("duplicate person user principal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("duplicate directory account user principal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Flags_Accounts_That_Share_A_Distinguished_Name()
    {
        // The shape the world-scoped identifier work left reachable: the second company's built-in
        // Administrator takes a disambiguated sAMAccountName and principal name, while its
        // distinguished name stays byte-identical to the first company's.
        var validator = new WorldInvariantValidator();
        var world = new SyntheticEnterpriseWorld();
        world.Accounts.AddRange(
        [
            new DirectoryAccount
            {
                Id = "A-1",
                CompanyId = "COMP-1",
                AccountType = "BuiltIn",
                SamAccountName = "Administrator",
                UserPrincipalName = "administrator@duckburg.test",
                DistinguishedName = "CN=Administrator,CN=Users,DC=duckburg,DC=test"
            },
            new DirectoryAccount
            {
                Id = "A-2",
                CompanyId = "COMP-2",
                AccountType = "BuiltIn",
                SamAccountName = "Administrator2",
                UserPrincipalName = "administrator2@duckburg.test",
                DistinguishedName = "CN=Administrator,CN=Users,DC=duckburg,DC=test"
            }
        ]);

        var result = validator.Validate(world);

        var distinguishedNameError = Assert.Single(
            result.Errors,
            error => error.Contains("duplicate directory account distinguished names", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("1 ", distinguishedNameError);
    }

    [Fact]
    public void Validate_Judges_An_Account_Value_That_Is_Not_A_Distinguished_Name()
    {
        // Until v0.13.0 cloud-joined device accounts carried a bare hostname here, so the check
        // filtered to values holding an attribute=value pair in order to skip them. Those accounts
        // now carry nothing, the filter is gone, and a value that is not a distinguished name is
        // once again a defect this reports rather than one it steps around.
        var validator = new WorldInvariantValidator();
        var world = new SyntheticEnterpriseWorld();
        world.Accounts.AddRange(
        [
            new DirectoryAccount
            {
                Id = "A-1",
                CompanyId = "COMP-1",
                AccountType = "Device",
                SamAccountName = "PAW-DOMAIN-001",
                UserPrincipalName = "PAW-DOMAIN-001@duckburg.test",
                DistinguishedName = "PAW-DOMAIN-001"
            },
            new DirectoryAccount
            {
                Id = "A-2",
                CompanyId = "COMP-2",
                AccountType = "Device",
                SamAccountName = "PAW-DOMAIN-001b",
                UserPrincipalName = "PAW-DOMAIN-001@mouseton.test",
                DistinguishedName = "PAW-DOMAIN-001"
            }
        ]);

        var result = validator.Validate(world);

        Assert.Single(
            result.Errors,
            error => error.Contains("duplicate directory account distinguished names", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Ignores_Accounts_That_State_No_Distinguished_Name()
    {
        var validator = new WorldInvariantValidator();
        var world = new SyntheticEnterpriseWorld();
        world.Accounts.AddRange(
        [
            new DirectoryAccount
            {
                Id = "A-1",
                CompanyId = "COMP-1",
                AccountType = "Device",
                IdentityProvider = "EntraID",
                SamAccountName = "PAW-DOMAIN-001",
                UserPrincipalName = "PAW-DOMAIN-001@duckburg.test",
                DistinguishedName = string.Empty
            },
            new DirectoryAccount
            {
                Id = "A-2",
                CompanyId = "COMP-2",
                AccountType = "Device",
                IdentityProvider = "EntraID",
                SamAccountName = "PAW-DOMAIN-001b",
                UserPrincipalName = "PAW-DOMAIN-001@mouseton.test",
                DistinguishedName = string.Empty
            }
        ]);

        var result = validator.Validate(world);

        Assert.DoesNotContain(result.Errors, error => error.Contains("distinguished name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Accepts_Accounts_Whose_Distinguished_Names_Are_Distinct()
    {
        var validator = new WorldInvariantValidator();
        var world = new SyntheticEnterpriseWorld();
        world.Accounts.AddRange(
        [
            new DirectoryAccount
            {
                Id = "A-1",
                CompanyId = "COMP-1",
                SamAccountName = "Administrator",
                UserPrincipalName = "administrator@duckburg.test",
                DistinguishedName = "CN=Administrator,CN=Users,DC=duckburg,DC=test"
            },
            new DirectoryAccount
            {
                Id = "A-2",
                CompanyId = "COMP-2",
                SamAccountName = "Administrator",
                UserPrincipalName = "administrator@mouseton.test",
                DistinguishedName = "CN=Administrator,CN=Users,DC=mouseton,DC=test"
            }
        ]);

        // A managed device repeats its own machine account's distinguished name, so the shared value
        // names one object and must not be read as a collision.
        world.Accounts.Add(new DirectoryAccount
        {
            Id = "A-3",
            CompanyId = "COMP-1",
            AccountType = "Device",
            SamAccountName = "DUCK-WS-001$",
            UserPrincipalName = "DUCK-WS-001$@duckburg.test",
            DistinguishedName = "CN=DUCK-WS-001,OU=Workstations,DC=duckburg,DC=test"
        });
        world.Devices.Add(new ManagedDevice
        {
            Id = "DEV-1",
            CompanyId = "COMP-1",
            Hostname = "DUCK-WS-001",
            DirectoryAccountId = "A-3",
            DistinguishedName = "CN=DUCK-WS-001,OU=Workstations,DC=duckburg,DC=test"
        });

        var result = validator.Validate(world);

        Assert.DoesNotContain(result.Errors, error => error.Contains("distinguished name", StringComparison.OrdinalIgnoreCase));
    }
}
