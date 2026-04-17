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
}
