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
        AppendIfPositive(
            errors,
            CountDuplicateValues(EnumerateDirectoryObjectDistinguishedNames(world)),
            "duplicate directory object distinguished names were generated across accounts, organizational units and groups.");
        AppendIfPositive(
            errors,
            CountAccessControlEntriesOnMirroredOrganizationalUnits(world),
            "access control entries name the container that mirrors an organizational unit instead of the unit itself.");
        AppendIfPositive(
            errors,
            CountOrganizationalUnitAccessControlEntriesWithoutScope(world),
            "access control entries on an organizational unit carry no inheritance scope.");

        return new WorldInvariantValidationResult
        {
            Errors = errors
        };
    }

    /// <summary>
    /// A distinguished name states where an object sits in a directory, so it names exactly one
    /// object and may never be issued twice. Scoped to accounts: managed devices and servers repeat
    /// their own machine account's distinguished name rather than naming a further object, so
    /// counting them again would report every domain-joined asset as a duplicate of itself.
    /// Every account value is read as issued: an object that sits in no directory tree carries an
    /// empty distinguished name, which <see cref="CountDuplicateValues"/> skips, and nothing is
    /// filtered out by shape. The shape filter this method used to apply existed only to tolerate
    /// cloud-joined device accounts carrying a bare hostname in the field, and those now carry
    /// nothing, so a value that is not a distinguished name is again a defect to report rather than
    /// one to step around.
    /// </summary>
    private static IEnumerable<string?> EnumerateAccountDistinguishedNames(SyntheticEnterpriseWorld world)
        => world.Accounts.Select(account => account.DistinguishedName);

    /// <summary>
    /// The same rule read across the directory object classes that name their own position: an
    /// account, an organizational unit and a group each occupy one place in the tree, and no two of
    /// them - of the same class or of different classes - may occupy the same one. Organizational
    /// units and groups were held out while a department name repeating across business units
    /// produced a colliding distinguished name; a department unit now sits under the unit of the
    /// business unit that owns it, and the groups derived from a repeated department name carry
    /// that business unit, so both classes name one object each and are judged here.
    ///
    /// Managed devices and server assets remain out, permanently and for a different reason: their
    /// distinguished name is a copy of their own machine account's, so pooling them would report
    /// every domain-joined asset as a duplicate of itself.
    /// </summary>
    private static IEnumerable<string?> EnumerateDirectoryObjectDistinguishedNames(SyntheticEnterpriseWorld world)
        => world.Accounts
            .Select(account => account.DistinguishedName)
            .Concat(world.OrganizationalUnits.Select(organizationalUnit => organizationalUnit.DistinguishedName))
            .Concat(world.Groups.Select(group => group.DistinguishedName));

    /// <summary>
    /// An entry relating to an organizational unit has one shape, so a reader selecting on the
    /// target type finds all of them and nothing else. An organizational unit is mirrored by a
    /// container so that a policy can be linked to it, but the entry belongs to the unit, and an
    /// entry naming the mirror would be a second shape for the same concept - the one thing a
    /// consumer deriving effective access cannot detect from the row in front of it.
    /// </summary>
    private static int CountAccessControlEntriesOnMirroredOrganizationalUnits(SyntheticEnterpriseWorld world)
    {
        var mirroredContainerIds = world.Containers
            .Where(container => string.Equals(container.SourceEntityType, nameof(DirectoryOrganizationalUnit), StringComparison.OrdinalIgnoreCase))
            .Select(container => container.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return world.AccessControlEvidence
            .Count(evidence => string.Equals(evidence.TargetType, "Container", StringComparison.OrdinalIgnoreCase)
                               && mirroredContainerIds.Contains(evidence.TargetId));
    }

    /// <summary>
    /// How far an entry on an organizational unit reaches is always stated, including when what it
    /// says is that the source did not record it. An empty field would leave a consumer to guess
    /// between "applies to this object only" and "not known", and a wrong guess yields a plausible
    /// effective-access answer that is simply wrong.
    /// </summary>
    private static int CountOrganizationalUnitAccessControlEntriesWithoutScope(SyntheticEnterpriseWorld world)
        => world.AccessControlEvidence
            .Count(evidence => string.Equals(evidence.TargetType, nameof(DirectoryOrganizationalUnit), StringComparison.OrdinalIgnoreCase)
                               && string.IsNullOrWhiteSpace(evidence.InheritanceScope));

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
