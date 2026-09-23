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
        AppendIfPositive(
            errors,
            CountDuplicateValues(world.Accounts.Select(account => account.Mail)),
            "duplicate directory account mail addresses were generated.");
        AppendIfPositive(
            errors,
            CountAccountMailTakenByAnotherAccountUpn(world),
            "directory account mail addresses collide with another account's user principal name.");
        AppendIfPositive(
            errors,
            CountDuplicateValues(EnumerateAccountDistinguishedNames(world)),
            "duplicate directory account distinguished names were generated.");

        return new WorldInvariantValidationResult
        {
            Errors = errors
        };
    }

    /// <summary>
    /// A distinguished name states where an object sits in a directory, so it names exactly one
    /// object and may never be issued twice. Scoped to accounts: managed devices and servers repeat
    /// their own machine account's distinguished name rather than naming a further object, so
    /// counting them again would report every domain-joined asset as a duplicate of itself, and
    /// organizational units and groups are left for a separate fix because department names repeat
    /// across business units today and their distinguished names are not yet qualified by one.
    /// Every account value is now read as issued: an object that sits in no directory tree carries
    /// an empty distinguished name, which <see cref="CountDuplicateValues"/> skips, and nothing is
    /// filtered out by shape. The shape filter this method used to apply existed only to tolerate
    /// cloud-joined device accounts carrying a bare hostname in the field, and those now carry
    /// nothing, so a value that is not a distinguished name would again be a defect to report
    /// rather than one to step around.
    /// </summary>
    private static IEnumerable<string?> EnumerateAccountDistinguishedNames(SyntheticEnterpriseWorld world)
        => world.Accounts.Select(account => account.DistinguishedName);

    private static int CountAccountMailTakenByAnotherAccountUpn(SyntheticEnterpriseWorld world)
    {
        var upnOwners = world.Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.UserPrincipalName))
            .GroupBy(account => account.UserPrincipalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(account => account.Id).ToArray(), StringComparer.OrdinalIgnoreCase);

        return world.Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Mail))
            .Where(account => upnOwners.TryGetValue(account.Mail!, out var owners)
                              && owners.Any(ownerId => !string.Equals(ownerId, account.Id, StringComparison.OrdinalIgnoreCase)))
            .Select(account => account.Mail!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
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
