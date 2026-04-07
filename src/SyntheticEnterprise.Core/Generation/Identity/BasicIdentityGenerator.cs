namespace SyntheticEnterprise.Core.Generation.Identity;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicIdentityGenerator : IIdentityGenerator
{
    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;

    public BasicIdentityGenerator(IIdFactory idFactory, IRandomSource randomSource)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
    }

    public void GenerateIdentity(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        foreach (var company in world.Companies)
        {
            var companyDefinition = context.Scenario.Companies.FirstOrDefault(c =>
                string.Equals(c.Name, company.Name, StringComparison.OrdinalIgnoreCase));

            if (companyDefinition is null)
            {
                continue;
            }

            var companyPeople = world.People.Where(p => p.CompanyId == company.Id).ToList();
            var companyDepartments = world.Departments.Where(d => d.CompanyId == company.Id).ToList();
            var rootDomain = BuildRootDomain(company.Name);

            var ous = CreateOus(company, companyDepartments, rootDomain);
            world.OrganizationalUnits.AddRange(ous);

            var peopleAccounts = CreateUserAccounts(company, companyPeople, ous, rootDomain);
            world.Accounts.AddRange(peopleAccounts);

            var managerMap = peopleAccounts.ToDictionary(a => a.PersonId ?? string.Empty, a => a.Id, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < world.Accounts.Count; i++)
            {
                var account = world.Accounts[i];
                if (account.CompanyId != company.Id || string.IsNullOrWhiteSpace(account.PersonId))
                {
                    continue;
                }

                var person = companyPeople.FirstOrDefault(p => p.Id == account.PersonId);
                if (person?.ManagerPersonId is not null && managerMap.TryGetValue(person.ManagerPersonId, out var managerAccountId))
                {
                    world.Accounts[i] = account with { ManagerAccountId = managerAccountId };
                }
            }

            var serviceAccounts = CreateServiceAccounts(company, companyDefinition, ous, rootDomain);
            world.Accounts.AddRange(serviceAccounts);

            var sharedAccounts = CreateSharedMailboxes(company, companyDefinition, ous, rootDomain);
            world.Accounts.AddRange(sharedAccounts);

            if (companyDefinition.IncludePrivilegedAccounts)
            {
                var privileged = CreatePrivilegedAccounts(company, companyPeople, ous, rootDomain);
                world.Accounts.AddRange(privileged);
            }

            var groups = CreateGroups(company, companyDepartments, ous, rootDomain, companyDefinition);
            world.Groups.AddRange(groups);

            var memberships = CreateMemberships(company, companyDepartments, companyPeople, groups, world.Accounts);
            world.GroupMemberships.AddRange(memberships);
        }
    }

    private List<DirectoryOrganizationalUnit> CreateOus(
        Company company,
        IReadOnlyList<Department> departments,
        string rootDomain)
    {
        var domainParts = rootDomain.Split('.');
        var dc = string.Join(",", domainParts.Select(part => $"DC={part}"));

        var root = new DirectoryOrganizationalUnit
        {
            Id = _idFactory.Next("OU"),
            CompanyId = company.Id,
            Name = "Corp",
            DistinguishedName = $"OU=Corp,{dc}",
            Purpose = "Root"
        };

        var users = new DirectoryOrganizationalUnit
        {
            Id = _idFactory.Next("OU"),
            CompanyId = company.Id,
            Name = "Users",
            ParentOuId = root.Id,
            DistinguishedName = $"OU=Users,{root.DistinguishedName}",
            Purpose = "User Accounts"
        };

        var service = new DirectoryOrganizationalUnit
        {
            Id = _idFactory.Next("OU"),
            CompanyId = company.Id,
            Name = "Service Accounts",
            ParentOuId = root.Id,
            DistinguishedName = $"OU=Service Accounts,{root.DistinguishedName}",
            Purpose = "Service Accounts"
        };

        var shared = new DirectoryOrganizationalUnit
        {
            Id = _idFactory.Next("OU"),
            CompanyId = company.Id,
            Name = "Shared Mailboxes",
            ParentOuId = root.Id,
            DistinguishedName = $"OU=Shared Mailboxes,{root.DistinguishedName}",
            Purpose = "Shared Mailboxes"
        };

        var groups = new DirectoryOrganizationalUnit
        {
            Id = _idFactory.Next("OU"),
            CompanyId = company.Id,
            Name = "Groups",
            ParentOuId = root.Id,
            DistinguishedName = $"OU=Groups,{root.DistinguishedName}",
            Purpose = "Groups"
        };

        var result = new List<DirectoryOrganizationalUnit> { root, users, service, shared, groups };

        foreach (var department in departments)
        {
            result.Add(new DirectoryOrganizationalUnit
            {
                Id = _idFactory.Next("OU"),
                CompanyId = company.Id,
                Name = department.Name,
                ParentOuId = users.Id,
                DistinguishedName = $"OU={EscapeDn(department.Name)},{users.DistinguishedName}",
                Purpose = "Department Users"
            });
        }

        return result;
    }

    private List<DirectoryAccount> CreateUserAccounts(
        Company company,
        IReadOnlyList<Person> people,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        string rootDomain)
    {
        var usersOu = ous.First(o => o.Name == "Users");
        var departmentOus = ous.Where(o => o.ParentOuId == usersOu.Id).ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);

        return people.Select(person =>
        {
            var sam = BuildSam(person.FirstName, person.LastName, person.EmployeeId);
            var targetOu = departmentOus.Values.FirstOrDefault(o =>
                string.Equals(o.Name, FindDepartmentName(person.DepartmentId, company.Id), StringComparison.OrdinalIgnoreCase))
                ?? usersOu;

            return new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                PersonId = person.Id,
                AccountType = "User",
                SamAccountName = sam,
                UserPrincipalName = person.UserPrincipalName,
                Mail = person.UserPrincipalName,
                DistinguishedName = $"CN={EscapeDn(person.DisplayName)},{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = false,
                MfaEnabled = true,
                EmployeeId = person.EmployeeId
            };
        }).ToList();
    }

    private string FindDepartmentName(string departmentId, string companyId) => departmentId;

    private List<DirectoryAccount> CreateServiceAccounts(
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        string rootDomain)
    {
        var targetOu = ous.First(o => o.Name == "Service Accounts");
        var results = new List<DirectoryAccount>();

        for (var i = 0; i < definition.ServiceAccountCount; i++)
        {
            var name = $"svc_{Slug(company.Name)}_{i + 1:00}";
            results.Add(new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                AccountType = "Service",
                SamAccountName = Truncate(name, 20),
                UserPrincipalName = $"{name}@{rootDomain}",
                Mail = null,
                DistinguishedName = $"CN={name},{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = _randomSource.NextDouble() < 0.25,
                MfaEnabled = false
            });
        }

        return results;
    }

    private List<DirectoryAccount> CreateSharedMailboxes(
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        string rootDomain)
    {
        var targetOu = ous.First(o => o.Name == "Shared Mailboxes");
        var mailboxPrefixes = new[] { "helpdesk", "payroll", "accounts-payable", "sales-ops", "recruiting", "facilities", "it-ops" };
        var results = new List<DirectoryAccount>();

        for (var i = 0; i < definition.SharedMailboxCount; i++)
        {
            var localPart = mailboxPrefixes[i % mailboxPrefixes.Length];
            results.Add(new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                AccountType = "Shared",
                SamAccountName = Truncate(localPart.Replace("-", ""), 20),
                UserPrincipalName = $"{localPart}@{rootDomain}",
                Mail = $"{localPart}@{rootDomain}",
                DistinguishedName = $"CN={EscapeDn(localPart)},{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = false,
                MfaEnabled = false
            });
        }

        return results;
    }

    private List<DirectoryAccount> CreatePrivilegedAccounts(
        Company company,
        IReadOnlyList<Person> people,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        string rootDomain)
    {
        var targetOu = ous.First(o => o.Name == "Service Accounts");
        var managers = people.Where(p =>
            p.Title.Contains("Chief", StringComparison.OrdinalIgnoreCase) ||
            p.Title.Contains("Vice President", StringComparison.OrdinalIgnoreCase) ||
            p.Title.Contains("Director", StringComparison.OrdinalIgnoreCase) ||
            p.Title.Contains("Manager", StringComparison.OrdinalIgnoreCase))
            .Take(Math.Max(2, people.Count / 20))
            .ToList();

        return managers.Select(person =>
        {
            var localPart = $"adm.{Slug(person.FirstName)}.{Slug(person.LastName)}";
            return new DirectoryAccount
            {
                Id = _idFactory.Next("ACT"),
                CompanyId = company.Id,
                PersonId = person.Id,
                AccountType = "Privileged",
                SamAccountName = Truncate($"adm_{Slug(person.LastName)}", 20),
                UserPrincipalName = $"{localPart}@{rootDomain}",
                Mail = null,
                DistinguishedName = $"CN={EscapeDn(person.DisplayName)} Admin,{targetOu.DistinguishedName}",
                OuId = targetOu.Id,
                Enabled = true,
                Privileged = true,
                MfaEnabled = _randomSource.NextDouble() >= 0.15,
                EmployeeId = person.EmployeeId
            };
        }).ToList();
    }

    private List<DirectoryGroup> CreateGroups(
        Company company,
        IReadOnlyList<Department> departments,
        IReadOnlyList<DirectoryOrganizationalUnit> ous,
        string rootDomain,
        ScenarioCompanyDefinition definition)
    {
        var groupsOu = ous.First(o => o.Name == "Groups");
        var result = new List<DirectoryGroup>();

        foreach (var department in departments)
        {
            var slug = Slug(department.Name);
            result.Add(new DirectoryGroup
            {
                Id = _idFactory.Next("GRP"),
                CompanyId = company.Id,
                Name = $"SG-{slug}-Users",
                GroupType = "Security",
                Scope = "Global",
                MailEnabled = false,
                DistinguishedName = $"CN=SG-{slug}-Users,{groupsOu.DistinguishedName}",
                OuId = groupsOu.Id,
                Purpose = $"Baseline access for {department.Name}"
            });

            result.Add(new DirectoryGroup
            {
                Id = _idFactory.Next("GRP"),
                CompanyId = company.Id,
                Name = $"DL-{slug}",
                GroupType = "Distribution",
                Scope = "Universal",
                MailEnabled = true,
                DistinguishedName = $"CN=DL-{slug},{groupsOu.DistinguishedName}",
                OuId = groupsOu.Id,
                Purpose = $"Mail distribution for {department.Name}"
            });
        }

        result.Add(new DirectoryGroup
        {
            Id = _idFactory.Next("GRP"),
            CompanyId = company.Id,
            Name = "SG-AllEmployees",
            GroupType = "Security",
            Scope = "Global",
            MailEnabled = false,
            DistinguishedName = $"CN=SG-AllEmployees,{groupsOu.DistinguishedName}",
            OuId = groupsOu.Id,
            Purpose = "All employee baseline access"
        });

        result.Add(new DirectoryGroup
        {
            Id = _idFactory.Next("GRP"),
            CompanyId = company.Id,
            Name = "M365-AllEmployees",
            GroupType = "M365",
            Scope = "Universal",
            MailEnabled = true,
            DistinguishedName = $"CN=M365-AllEmployees,{groupsOu.DistinguishedName}",
            OuId = groupsOu.Id,
            Purpose = "Collaboration membership"
        });

        return result;
    }

    private List<DirectoryGroupMembership> CreateMemberships(
        Company company,
        IReadOnlyList<Department> departments,
        IReadOnlyList<Person> people,
        IReadOnlyList<DirectoryGroup> groups,
        IReadOnlyList<DirectoryAccount> accounts)
    {
        var results = new List<DirectoryGroupMembership>();
        var userAccounts = accounts.Where(a => a.CompanyId == company.Id && a.AccountType == "User").ToList();

        var allEmployeesGroup = groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "SG-AllEmployees");
        var m365Group = groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == "M365-AllEmployees");

        foreach (var account in userAccounts)
        {
            if (allEmployeesGroup is not null)
            {
                results.Add(new DirectoryGroupMembership
                {
                    Id = _idFactory.Next("MEM"),
                    GroupId = allEmployeesGroup.Id,
                    MemberObjectId = account.Id,
                    MemberObjectType = "Account"
                });
            }

            if (m365Group is not null)
            {
                results.Add(new DirectoryGroupMembership
                {
                    Id = _idFactory.Next("MEM"),
                    GroupId = m365Group.Id,
                    MemberObjectId = account.Id,
                    MemberObjectType = "Account"
                });
            }
        }

        foreach (var department in departments)
        {
            var sg = groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == $"SG-{Slug(department.Name)}-Users");
            var dl = groups.FirstOrDefault(g => g.CompanyId == company.Id && g.Name == $"DL-{Slug(department.Name)}");

            var deptPeople = people.Where(p => p.CompanyId == company.Id && p.DepartmentId == department.Id).ToList();
            foreach (var person in deptPeople)
            {
                var account = userAccounts.FirstOrDefault(a => a.PersonId == person.Id);
                if (account is null) continue;

                if (sg is not null)
                {
                    results.Add(new DirectoryGroupMembership
                    {
                        Id = _idFactory.Next("MEM"),
                        GroupId = sg.Id,
                        MemberObjectId = account.Id,
                        MemberObjectType = "Account"
                    });
                }

                if (dl is not null)
                {
                    results.Add(new DirectoryGroupMembership
                    {
                        Id = _idFactory.Next("MEM"),
                        GroupId = dl.Id,
                        MemberObjectId = account.Id,
                        MemberObjectType = "Account"
                    });
                }
            }
        }

        return results;
    }

    private static string BuildRootDomain(string companyName)
    {
        var normalized = new string(companyName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "example.test" : normalized + ".test";
    }

    private static string BuildSam(string firstName, string lastName, string employeeId)
    {
        var baseValue = $"{Slug(firstName).FirstOrDefault()}{Slug(lastName)}";
        return Truncate($"{baseValue}{employeeId[^3..]}", 20);
    }

    private static string Slug(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string EscapeDn(string value)
        => value.Replace(",", "\\,");

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
