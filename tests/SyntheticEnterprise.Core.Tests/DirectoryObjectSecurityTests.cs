using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class DirectoryObjectSecurityTests
{
    private const string OuTargetType = nameof(DirectoryOrganizationalUnit);

    [Fact]
    public void OrganizationalUnitSecurity_CarriesInheritanceScopeOnEveryEntry()
    {
        var world = GenerateWorld();

        var entries = OuEntries(world);
        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            // Never empty on an entry naming an organizational unit. NotRecorded is a stated value
            // and not a gap: it says the propagation flag this entry carried was not collected, so a
            // reader must not resolve it either way.
            Assert.Contains(
                entry.InheritanceScope,
                new[]
                {
                    AccessControlInheritanceScope.ThisObjectOnly,
                    AccessControlInheritanceScope.ThisObjectAndAllDescendants,
                    AccessControlInheritanceScope.NotRecorded
                });
            Assert.Contains(entry.TargetId, world.OrganizationalUnits.Select(ou => ou.Id));
        });
    }

    [Fact]
    public void OrganizationalUnitSecurity_EmitsOnlyExplicitEntries()
    {
        var world = GenerateWorld();

        var entries = OuEntries(world);
        Assert.All(entries, entry =>
        {
            Assert.False(entry.IsInherited);
            Assert.Null(entry.InheritanceSourceId);
        });

        var ouById = world.OrganizationalUnits.ToDictionary(ou => ou.Id, StringComparer.Ordinal);

        // An entry that reaches a descendant by inheritance must be recorded once, where it is set.
        // No descendant may carry a second copy of the same principal, right and access type.
        foreach (var inheriting in entries.Where(entry =>
                     entry.InheritanceScope == AccessControlInheritanceScope.ThisObjectAndAllDescendants))
        {
            var descendantIds = DescendantIds(world, inheriting.TargetId);
            var repeated = entries.Where(entry =>
                descendantIds.Contains(entry.TargetId)
                && entry.PrincipalObjectId == inheriting.PrincipalObjectId
                && entry.RightName == inheriting.RightName
                && entry.AccessType == inheriting.AccessType);

            Assert.Empty(repeated.Select(entry => $"{entry.RightName} on {ouById[entry.TargetId].DistinguishedName}"));
        }
    }

    [Fact]
    public void OrganizationalUnitSecurity_CoversInheritingAndThisObjectOnlyScopes()
    {
        var world = GenerateWorld();
        var entries = OuEntries(world);

        var inheriting = entries.Where(entry =>
            entry.InheritanceScope == AccessControlInheritanceScope.ThisObjectAndAllDescendants).ToList();
        Assert.NotEmpty(inheriting);

        // An inheriting entry is only meaningful where the organizational unit it is set on has
        // descendants for it to reach.
        Assert.Contains(inheriting, entry => DescendantIds(world, entry.TargetId).Count > 0);

        Assert.Contains(entries, entry => entry.InheritanceScope == AccessControlInheritanceScope.ThisObjectOnly);
    }

    [Fact]
    public void OrganizationalUnitSecurity_PlacesAProtectedUnitBelowAnInheritingEntry()
    {
        var world = GenerateWorld();
        var entries = OuEntries(world);
        var ouById = world.OrganizationalUnits.ToDictionary(ou => ou.Id, StringComparer.Ordinal);

        var protectedUnits = world.OrganizationalUnits.Where(ou => ou.DaclInheritanceProtected).ToList();
        Assert.NotEmpty(protectedUnits);

        var inheritingTargetIds = entries
            .Where(entry => entry.InheritanceScope == AccessControlInheritanceScope.ThisObjectAndAllDescendants)
            .Select(entry => entry.TargetId)
            .ToHashSet(StringComparer.Ordinal);

        var qualifying = protectedUnits.Where(unit =>
        {
            var ancestors = AncestorIds(ouById, unit).ToList();
            if (!ancestors.Any(inheritingTargetIds.Contains))
            {
                return false;
            }

            // Descendants above the protected unit: at least one organizational unit strictly
            // between the unit carrying the inheriting entry and the protected unit, which does
            // receive the inherited entry.
            var highestInheriting = ancestors.Last(inheritingTargetIds.Contains);
            var above = ancestors.TakeWhile(id => id != highestInheriting).ToList();

            // Descendants below the protected unit, which do not receive it.
            var below = DescendantIds(world, unit.Id);

            return above.Count > 0 && below.Count > 0;
        }).ToList();

        Assert.NotEmpty(qualifying);
    }

    [Fact]
    public void OrganizationalUnitSecurity_ProtectedUnitDoesNotBlockPolicyLinkInheritance()
    {
        var world = GenerateWorld();

        // The two flags are independent aspects of the same object. The protected unit must not be
        // reported as blocking Group Policy link inheritance merely because its access control list
        // is protected.
        var protectedUnits = world.OrganizationalUnits.Where(ou => ou.DaclInheritanceProtected).ToList();
        Assert.NotEmpty(protectedUnits);

        foreach (var unit in protectedUnits)
        {
            var container = world.Containers.Single(candidate =>
                candidate.SourceEntityType == nameof(DirectoryOrganizationalUnit)
                && candidate.SourceEntityId == unit.Id);
            Assert.False(container.BlocksPolicyInheritance);
        }

        // And no access control entry names policy link blocking as a right.
        Assert.DoesNotContain(
            world.AccessControlEvidence,
            entry => entry.RightName.Contains("BlockInheritance", StringComparison.OrdinalIgnoreCase));
    }

    private static List<AccessControlEvidenceRecord> OuEntries(SyntheticEnterpriseWorld world)
        => world.AccessControlEvidence
            .Where(entry => entry.TargetType == OuTargetType)
            .ToList();

    private static IEnumerable<string> AncestorIds(
        IReadOnlyDictionary<string, DirectoryOrganizationalUnit> ouById,
        DirectoryOrganizationalUnit unit)
    {
        var current = unit.ParentOuId;
        while (!string.IsNullOrWhiteSpace(current) && ouById.TryGetValue(current, out var parent))
        {
            yield return parent.Id;
            current = parent.ParentOuId;
        }
    }

    private static HashSet<string> DescendantIds(SyntheticEnterpriseWorld world, string ouId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>();
        frontier.Enqueue(ouId);

        while (frontier.Count > 0)
        {
            var next = frontier.Dequeue();
            foreach (var child in world.OrganizationalUnits.Where(ou => ou.ParentOuId == next))
            {
                if (result.Add(child.Id))
                {
                    frontier.Enqueue(child.Id);
                }
            }
        }

        return result;
    }

    private static SyntheticEnterpriseWorld GenerateWorld()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        return generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Directory Object Security Test",
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Directory Object Security Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 160,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            SharedMailboxCount = 3,
                            ServiceAccountCount = 5,
                            IncludePrivilegedAccounts = true,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet()).World;
    }
}
