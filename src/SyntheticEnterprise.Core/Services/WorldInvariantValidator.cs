namespace SyntheticEnterprise.Core.Services;

using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class WorldInvariantValidator : IWorldInvariantValidator
{
    public WorldInvariantValidationResult Validate(SyntheticEnterpriseWorld world)
    {
        var errors = new List<string>();

        AppendIfPositive(
            errors,
            CountDuplicateValues(world.People.Select(person => person.UserPrincipalName)),
            "duplicate person user principal names were generated.");
        AppendIfPositive(
            errors,
            CountDuplicateValues(world.Accounts.Select(account => account.UserPrincipalName)),
            "duplicate directory account user principal names were generated.");

        return new WorldInvariantValidationResult
        {
            Errors = errors
        };
    }

    private static int CountDuplicateValues(IEnumerable<string?> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);

    private static void AppendIfPositive(ICollection<string> errors, int count, string message)
    {
        if (count > 0)
        {
            errors.Add($"World invariant validation: {count} {message}");
        }
    }
}
