namespace SyntheticEnterprise.Core.Generation.Infrastructure;

using System.Security.Cryptography;
using System.Text;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicInfrastructureGenerator : IInfrastructureGenerator
{
    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;
    private readonly IClock _clock;

    public BasicInfrastructureGenerator(IIdFactory idFactory, IRandomSource randomSource, IClock clock)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
        _clock = clock;
    }

    public void GenerateInfrastructure(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        EnsureSoftwareCatalog(world, catalogs);

        foreach (var company in world.Companies)
        {
            var definition = context.Scenario.Companies.FirstOrDefault(c =>
                string.Equals(c.Name, company.Name, StringComparison.OrdinalIgnoreCase));

            if (definition is null)
            {
                continue;
            }

            var people = world.People.Where(p => p.CompanyId == company.Id).ToList();
            var offices = world.Offices.Where(o => o.CompanyId == company.Id).ToList();
            var teams = world.Teams.Where(t => t.CompanyId == company.Id).ToList();
            var privilegedAccounts = world.Accounts.Where(a => a.CompanyId == company.Id && a.AccountType == "Privileged").ToList();
            var ous = world.OrganizationalUnits.Where(o => o.CompanyId == company.Id).ToList();

            var userAccounts = world.Accounts.Where(a => a.CompanyId == company.Id && a.AccountType == "User").ToList();
            CreateWorkstations(world, company, definition, people, userAccounts, privilegedAccounts, ous);
            CreateServers(world, company, definition, offices, teams, ous);
            CreateNetworkAssets(world, company, definition, offices);
            CreateTelephonyAssets(world, company, definition, people, offices);
            CreateSoftwareInstallations(world, company);
            CreateAdministrativeEndpointControls(world, company, privilegedAccounts);
            CreateEndpointPolicyAndLocalGroupState(world, company);
        }
    }

    private void EnsureSoftwareCatalog(SyntheticEnterpriseWorld world, CatalogSet catalogs)
    {
        if (world.SoftwarePackages.Count > 0)
        {
            return;
        }

        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (catalogs.CsvCatalogs.TryGetValue("software_catalog", out var rows))
        {
            foreach (var row in rows)
            {
                var name = Read(row, "Name");
                if (string.IsNullOrWhiteSpace(name) || !added.Add(name))
                {
                    continue;
                }

                world.SoftwarePackages.Add(new SoftwarePackage
                {
                    Id = _idFactory.Next("SW"),
                    Name = name,
                    Category = Read(row, "Category"),
                    Vendor = Read(row, "Vendor"),
                    Version = Read(row, "Version")
                });
            }
        }

        if (world.SoftwarePackages.Count == 0)
        {
            var fallback = new[]
            {
                ("Microsoft 365 Apps", "Productivity", "Microsoft", "2408"),
                ("Microsoft Teams", "Collaboration", "Microsoft", "2.1"),
                ("Google Chrome", "Browser", "Google", "135"),
                ("Microsoft Edge", "Browser", "Microsoft", "135"),
                ("CrowdStrike Falcon", "Security", "CrowdStrike", "7.2"),
                ("Cisco AnyConnect", "VPN", "Cisco", "5.1"),
                ("Visual Studio Code", "Developer", "Microsoft", "1.99"),
                ("7-Zip", "Utility", "7-Zip", "24.09"),
                ("SQL Server", "Database", "Microsoft", "2022"),
                ("IIS", "Web", "Microsoft", "10.0"),
                ("Windows Server Backup", "Backup", "Microsoft", "10.0"),
                ("VMware Tools", "Virtualization", "VMware", "12.5")
            };

            foreach (var item in fallback)
            {
                world.SoftwarePackages.Add(new SoftwarePackage
                {
                    Id = _idFactory.Next("SW"),
                    Name = item.Item1,
                    Category = item.Item2,
                    Vendor = item.Item3,
                    Version = item.Item4
                });
            }
        }
    }

    private void CreateWorkstations(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Person> people,
        IReadOnlyList<DirectoryAccount> userAccounts,
        IReadOnlyList<DirectoryAccount> privilegedAccounts,
        IReadOnlyList<DirectoryOrganizationalUnit> ous)
    {
        var target = Math.Max(1, (int)Math.Round(people.Count * Math.Clamp(definition.WorkstationCoverageRatio, 0.1, 1.5)));
        var models = new[]
        {
            ("Dell", "Latitude 7450", "Windows 11 Enterprise", "23H2"),
            ("Lenovo", "ThinkPad T14", "Windows 11 Enterprise", "23H2"),
            ("HP", "EliteBook 840", "Windows 11 Enterprise", "23H2"),
            ("Apple", "MacBook Pro 14", "macOS", "14.7")
        };
        var workstationOu = ous.FirstOrDefault(ou => ou.Name == "Workstations");
        var privilegedAccessWorkstationOu = ous.FirstOrDefault(ou => ou.Name == "Privileged Access Workstations");

        for (var i = 0; i < target; i++)
        {
            var person = people[i % people.Count];
            var account = userAccounts.FirstOrDefault(a => a.PersonId == person.Id);
            var model = models[i % models.Length];
            var hostname = BuildHostname(company.Name, person.LastName, i + 1, "WS");
            var joinProfile = ResolveWorkstationJoinProfile(model.Item3, contextSupportsHybridDirectory: world.IdentityStores.Any(store =>
                store.CompanyId == company.Id &&
                string.Equals(store.StoreType, "EntraTenant", StringComparison.OrdinalIgnoreCase)));
            var accountLinks = CreateMachineAccounts(
                world,
                company,
                hostname,
                workstationOu,
                administrativeTier: null,
                createOnPremAccount: joinProfile.CreateOnPremAccount,
                createCloudAccount: joinProfile.CreateCloudAccount);

            world.Devices.Add(new ManagedDevice
            {
                Id = _idFactory.Next("DEV"),
                CompanyId = company.Id,
                DeviceType = "Workstation",
                Hostname = hostname,
                AssetTag = $"AT-{company.Name[..Math.Min(3, company.Name.Length)].ToUpperInvariant()}-{i + 1000}",
                SerialNumber = $"SN{company.Id[^4..]}{i + 100000}",
                Manufacturer = model.Item1,
                Model = model.Item2,
                OperatingSystem = model.Item3,
                OperatingSystemVersion = model.Item4,
                AssignedPersonId = person.Id,
                AssignedOfficeId = person.OfficeId,
                DirectoryAccountId = account?.Id,
                OnPremDirectoryAccountId = accountLinks.OnPremAccountId,
                CloudDirectoryAccountId = accountLinks.CloudAccountId,
                OuId = workstationOu?.Id,
                DistinguishedName = workstationOu is null ? null : $"CN={hostname},{workstationOu.DistinguishedName}",
                DomainJoined = joinProfile.CreateOnPremAccount,
                ComplianceState = _randomSource.NextDouble() < 0.92 ? "Compliant" : "NonCompliant",
                LastSeen = _clock.UtcNow.AddDays(-_randomSource.Next(0, 45))
            });
        }

        if (privilegedAccessWorkstationOu is null || privilegedAccounts.Count == 0)
        {
            return;
        }

        var privilegedPeople = privilegedAccounts
            .Where(account => !string.IsNullOrWhiteSpace(account.PersonId))
            .Select(account => new
            {
                Account = account,
                Person = people.FirstOrDefault(person => string.Equals(person.Id, account.PersonId, StringComparison.OrdinalIgnoreCase))
            })
            .Where(entry => entry.Person is not null)
            .DistinctBy(entry => entry.Person!.Id)
            .Take(Math.Clamp((int)Math.Ceiling(privilegedAccounts.Count / 8.0), 1, Math.Max(1, privilegedAccounts.Count)))
            .ToList();

        for (var i = 0; i < privilegedPeople.Count; i++)
        {
            var privilegedPerson = privilegedPeople[i].Person!;
            var privilegedAccount = privilegedPeople[i].Account;
            var hostname = BuildHostname(company.Name, privilegedPerson.LastName, i + 1, "PAW");
            var accountLinks = CreateMachineAccounts(
                world,
                company,
                hostname,
                privilegedAccessWorkstationOu,
                administrativeTier: "Tier1",
                createOnPremAccount: true,
                createCloudAccount: true);

            world.Devices.Add(new ManagedDevice
            {
                Id = _idFactory.Next("DEV"),
                CompanyId = company.Id,
                DeviceType = "PrivilegedAccessWorkstation",
                Hostname = hostname,
                AssetTag = $"PAW-{company.Name[..Math.Min(3, company.Name.Length)].ToUpperInvariant()}-{i + 500}",
                SerialNumber = $"PWSN{company.Id[^4..]}{i + 700000}",
                Manufacturer = "Dell",
                Model = "Precision 7680",
                OperatingSystem = "Windows 11 Enterprise",
                OperatingSystemVersion = "23H2",
                AssignedPersonId = privilegedPerson.Id,
                AssignedOfficeId = privilegedPerson.OfficeId,
                DirectoryAccountId = privilegedAccount.Id,
                OnPremDirectoryAccountId = accountLinks.OnPremAccountId,
                CloudDirectoryAccountId = accountLinks.CloudAccountId,
                OuId = privilegedAccessWorkstationOu.Id,
                DistinguishedName = $"CN={hostname},{privilegedAccessWorkstationOu.DistinguishedName}",
                DomainJoined = true,
                ComplianceState = "Compliant",
                LastSeen = _clock.UtcNow.AddDays(-_randomSource.Next(0, 10))
            });
        }
    }

    private void CreateServers(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Office> offices,
        IReadOnlyList<Team> teams,
        IReadOnlyList<DirectoryOrganizationalUnit> ous)
    {
        var serverEnvironments = ous
            .Where(ou => ou.ParentOuId is not null)
            .GroupBy(ou => ou.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var serversOu = ous.FirstOrDefault(ou => ou.Name == "Servers");
        var targetServerCount = Math.Max(1, definition.ServerCount);
        var jumpHostCount = targetServerCount >= 10 ? 1 : 0;
        var primaryServerCount = Math.Max(1, targetServerCount - jumpHostCount);
        var officePlan = BuildServerOfficePlan(offices, primaryServerCount);
        var rolePlan = BuildServerRolePlan(primaryServerCount, offices.Count);

        for (var i = 0; i < primaryServerCount; i++)
        {
            var office = officePlan.Count > 0 ? officePlan[i % officePlan.Count] : null;
            var role = rolePlan[i % rolePlan.Count];
            var team = SelectServerOwnerTeam(teams, role);
            var environment = ResolveServerEnvironment(role, i, primaryServerCount);
            var targetOu = serverEnvironments.TryGetValue(environment, out var environmentOu)
                ? environmentOu
                : serversOu;
            var hostname = BuildServerHostname(company.Name, role, i + 1);
            var accountLinks = CreateMachineAccounts(
                world,
                company,
                hostname,
                targetOu,
                administrativeTier: string.Equals(role, "Domain Controller", StringComparison.OrdinalIgnoreCase) ? "Tier0" : "Tier1",
                createOnPremAccount: true,
                createCloudAccount: false);

            world.Servers.Add(new ServerAsset
            {
                Id = _idFactory.Next("SRV"),
                CompanyId = company.Id,
                Hostname = hostname,
                ServerRole = role,
                Environment = environment,
                OperatingSystem = "Windows Server",
                OperatingSystemVersion = i % 3 == 0 ? "2022" : "2019",
                OfficeId = office?.Id ?? "",
                DirectoryAccountId = accountLinks.PrimaryAccountId,
                OnPremDirectoryAccountId = accountLinks.OnPremAccountId,
                CloudDirectoryAccountId = accountLinks.CloudAccountId,
                OuId = targetOu?.Id,
                DistinguishedName = targetOu is null ? null : $"CN={hostname},{targetOu.DistinguishedName}",
                DomainJoined = true,
                OwnerTeamId = team?.Id ?? "",
                Criticality = i % 5 == 0 ? "High" : "Medium"
            });
        }

        if (jumpHostCount == 1)
        {
            var office = offices.FirstOrDefault();
            var team = teams.FirstOrDefault();
            var targetOu = serverEnvironments.TryGetValue("Production", out var productionOu)
                ? productionOu
                : serversOu;
            var hostname = BuildServerHostname(company.Name, "Jump Host", targetServerCount);
            var accountLinks = CreateMachineAccounts(
                world,
                company,
                hostname,
                targetOu,
                administrativeTier: "Tier1",
                createOnPremAccount: true,
                createCloudAccount: false);

            world.Servers.Add(new ServerAsset
            {
                Id = _idFactory.Next("SRV"),
                CompanyId = company.Id,
                Hostname = hostname,
                ServerRole = "Jump Host",
                Environment = "Production",
                OperatingSystem = "Windows Server",
                OperatingSystemVersion = "2022",
                OfficeId = office?.Id ?? "",
                DirectoryAccountId = accountLinks.PrimaryAccountId,
                OnPremDirectoryAccountId = accountLinks.OnPremAccountId,
                CloudDirectoryAccountId = accountLinks.CloudAccountId,
                OuId = targetOu?.Id,
                DistinguishedName = targetOu is null ? null : $"CN={hostname},{targetOu.DistinguishedName}",
                DomainJoined = true,
                OwnerTeamId = team?.Id ?? "",
                Criticality = "High"
            });
        }
    }

    private List<string> BuildServerRolePlan(int serverCount, int officeCount)
    {
        if (serverCount <= 0)
        {
            return new();
        }

        var plan = new List<string>();
        AddServerRoles(plan, "Domain Controller", Math.Max(2, Math.Min(4, 1 + officeCount / 3)));
        AddServerRoles(plan, "Application Server", Math.Max(1, serverCount / 4));
        AddServerRoles(plan, "SQL Server", Math.Max(1, serverCount / 6));
        AddServerRoles(plan, "File Server", Math.Max(1, officeCount / 2));
        AddServerRoles(plan, "Web Server", Math.Max(1, serverCount / 7));
        AddServerRoles(plan, "Print Server", officeCount >= 2 ? 1 : 0);
        AddServerRoles(plan, "Management Server", serverCount >= 8 ? Math.Max(1, serverCount / 12) : 0);
        AddServerRoles(plan, "Remote Access Server", officeCount >= 3 && serverCount >= 12 ? 1 : 0);

        while (plan.Count < serverCount)
        {
            AddServerRoles(plan, "Application Server", 1);
            if (plan.Count < serverCount)
            {
                AddServerRoles(plan, "Web Server", 1);
            }

            if (plan.Count < serverCount)
            {
                AddServerRoles(plan, "SQL Server", 1);
            }
        }

        return InterleaveServerRoles(plan.Take(serverCount).ToList());
    }

    private static void AddServerRoles(List<string> plan, string role, int count)
    {
        for (var i = 0; i < count; i++)
        {
            plan.Add(role);
        }
    }

    private static List<string> InterleaveServerRoles(IReadOnlyList<string> plan)
    {
        var counts = plan
            .GroupBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var order = plan
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var interleaved = new List<string>(plan.Count);

        while (interleaved.Count < plan.Count)
        {
            foreach (var role in order)
            {
                if (!counts.TryGetValue(role, out var remaining) || remaining <= 0)
                {
                    continue;
                }

                interleaved.Add(role);
                counts[role] = remaining - 1;
            }
        }

        return interleaved;
    }

    private static Team? SelectServerOwnerTeam(IReadOnlyList<Team> teams, string role)
    {
        if (teams.Count == 0)
        {
            return null;
        }

        static bool Matches(Team team, params string[] tokens)
            => tokens.Any(token => team.Name.Contains(token, StringComparison.OrdinalIgnoreCase));

        return role switch
        {
            "Domain Controller" => teams.FirstOrDefault(team => Matches(team, "Identity", "Directory"))
                                  ?? teams.FirstOrDefault(team => Matches(team, "Platform", "Infrastructure")),
            "SQL Server" => teams.FirstOrDefault(team => Matches(team, "Data", "Database", "Analytics"))
                            ?? teams.FirstOrDefault(team => Matches(team, "Platform")),
            "Web Server" => teams.FirstOrDefault(team => Matches(team, "Platform", "Engineering", "Digital"))
                            ?? teams.FirstOrDefault(team => Matches(team, "Application")),
            "Application Server" => teams.FirstOrDefault(team => Matches(team, "Application", "Platform", "Engineering"))
                                    ?? teams.FirstOrDefault(team => Matches(team, "Automation")),
            "Management Server" => teams.FirstOrDefault(team => Matches(team, "Infrastructure", "Platform", "Operations"))
                                   ?? teams.FirstOrDefault(team => Matches(team, "Service Desk")),
            "Remote Access Server" => teams.FirstOrDefault(team => Matches(team, "Infrastructure", "Security", "Platform")),
            "File Server" or "Print Server" => teams.FirstOrDefault(team => Matches(team, "Infrastructure", "Service Desk", "Operations")),
            _ => teams.FirstOrDefault()
        };
    }

    private static string ResolveServerEnvironment(string role, int index, int totalCount)
    {
        if (string.Equals(role, "Domain Controller", StringComparison.OrdinalIgnoreCase))
        {
            return "Production";
        }

        if (index >= Math.Max(4, totalCount - 2))
        {
            return "Development";
        }

        if (index >= Math.Max(3, totalCount - 5))
        {
            return "Staging";
        }

        return "Production";
    }

    private List<Office> BuildServerOfficePlan(IReadOnlyList<Office> offices, int serverCount)
    {
        if (offices.Count == 0 || serverCount <= 0)
        {
            return new();
        }

        var weights = offices
            .Select(office => new
            {
                Office = office,
                Weight = office.IsHeadquarters
                    ? 7.0
                    : string.Equals(office.Country, offices[0].Country, StringComparison.OrdinalIgnoreCase)
                        ? 2.5
                        : 1.0
            })
            .ToList();

        var totalWeight = weights.Sum(item => item.Weight);
        var allocations = offices.ToDictionary(office => office.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var item in weights)
        {
            allocations[item.Office.Id] = Math.Max(
                item.Office.IsHeadquarters ? 1 : 0,
                (int)Math.Floor((item.Weight / totalWeight) * serverCount));
        }

        var allocated = allocations.Values.Sum();
        var index = 0;
        while (allocated < serverCount)
        {
            var office = weights[index % weights.Count].Office;
            allocations[office.Id] = allocations[office.Id] + 1;
            allocated++;
            index++;
        }

        while (allocated > serverCount)
        {
            var office = weights
                .OrderBy(item => item.Office.IsHeadquarters ? 1 : 0)
                .ThenByDescending(item => allocations[item.Office.Id])
                .FirstOrDefault(item => allocations[item.Office.Id] > (item.Office.IsHeadquarters ? 1 : 0))
                ?.Office;
            if (office is null)
            {
                break;
            }

            allocations[office.Id] = allocations[office.Id] - 1;
            allocated--;
        }

        return weights
            .OrderByDescending(item => item.Office.IsHeadquarters)
            .ThenByDescending(item => item.Weight)
            .SelectMany(item => Enumerable.Repeat(item.Office, allocations[item.Office.Id]))
            .ToList();
    }

    private void CreateNetworkAssets(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Office> offices)
    {
        var networkProfiles = new[]
        {
            new NetworkAssetProfile("Switch", "Cisco", "Catalyst 9300", "SW"),
            new NetworkAssetProfile("Router", "Cisco", "ISR 4451", "RTR"),
            new NetworkAssetProfile("Firewall", "Palo Alto", "PA-3410", "FW"),
            new NetworkAssetProfile("Wireless Controller", "Cisco", "Catalyst 9800", "WLC"),
            new NetworkAssetProfile("Access Point", "Aruba", "AP-635", "AP"),
            new NetworkAssetProfile("Load Balancer", "F5", "BIG-IP i2600", "LB")
        };

        foreach (var office in offices)
        {
            for (var i = 0; i < Math.Max(1, definition.NetworkAssetCountPerOffice); i++)
            {
                var profile = networkProfiles[i % networkProfiles.Length];
                world.NetworkAssets.Add(new NetworkAsset
                {
                    Id = _idFactory.Next("NET"),
                    CompanyId = company.Id,
                    AssetType = profile.AssetType,
                    Hostname = BuildNetworkHostname(company.Name, office.City, i + 1, profile.HostPrefix),
                    OfficeId = office.Id,
                    Vendor = profile.Vendor,
                    Model = profile.Model
                });
            }
        }
    }

    private void CreateTelephonyAssets(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Person> people,
        IReadOnlyList<Office> offices)
    {
        var count = Math.Max(1, definition.TelephonyAssetCountPerOffice * Math.Max(1, offices.Count));
        for (var i = 0; i < count; i++)
        {
            var person = people.Count > 0 ? people[i % people.Count] : null;
            var office = person is not null && !string.IsNullOrWhiteSpace(person.OfficeId)
                ? offices.FirstOrDefault(o => o.Id == person.OfficeId)
                : (offices.Count > 0 ? offices[i % offices.Count] : null);

            world.TelephonyAssets.Add(new TelephonyAsset
            {
                Id = _idFactory.Next("TEL"),
                CompanyId = company.Id,
                AssetType = i % 5 == 0 ? "Conference Phone" : "DeskPhone",
                Identifier = $"+1-555-{_randomSource.Next(200,999)}-{_randomSource.Next(1000,9999)}",
                AssignedPersonId = i % 5 == 0 ? null : person?.Id,
                AssignedOfficeId = office?.Id
            });
        }
    }

    private void CreateSoftwareInstallations(SyntheticEnterpriseWorld world, Company company)
    {
        var companySoftware = world.SoftwarePackages.ToList();
        var devices = world.Devices.Where(d => d.CompanyId == company.Id).ToList();
        var servers = world.Servers.Where(s => s.CompanyId == company.Id).ToList();

        var workstationDefaults = companySoftware
            .Where(s => s.Category is "Productivity" or "Collaboration" or "Browser" or "Security" or "VPN" or "Utility")
            .ToList();

        var serverDefaults = companySoftware
            .Where(s => s.Category is "Database" or "Web" or "Backup" or "Security" or "Virtualization" or "Utility")
            .ToList();

        foreach (var device in devices)
        {
            foreach (var software in workstationDefaults.Take(5 + _randomSource.Next(0, 3)))
            {
                world.DeviceSoftwareInstallations.Add(new DeviceSoftwareInstallation
                {
                    Id = _idFactory.Next("DSW"),
                    DeviceId = device.Id,
                    SoftwareId = software.Id
                });
            }

            if (device.Model.Contains("MacBook", StringComparison.OrdinalIgnoreCase))
            {
                var vscode = companySoftware.FirstOrDefault(s => s.Name == "Visual Studio Code");
                if (vscode is not null)
                {
                    world.DeviceSoftwareInstallations.Add(new DeviceSoftwareInstallation
                    {
                        Id = _idFactory.Next("DSW"),
                        DeviceId = device.Id,
                        SoftwareId = vscode.Id
                    });
                }
            }
        }

        foreach (var server in servers)
        {
            foreach (var software in serverDefaults.Take(3 + _randomSource.Next(0, 2)))
            {
                world.ServerSoftwareInstallations.Add(new ServerSoftwareInstallation
                {
                    Id = _idFactory.Next("SSW"),
                    ServerId = server.Id,
                    SoftwareId = software.Id
                });
            }
        }
    }

    private void CreateAdministrativeEndpointControls(
        SyntheticEnterpriseWorld world,
        Company company,
        IReadOnlyList<DirectoryAccount> privilegedAccounts)
    {
        var groups = world.Groups.Where(group => group.CompanyId == company.Id).ToList();
        var devices = world.Devices.Where(device => device.CompanyId == company.Id).ToList();
        var servers = world.Servers.Where(server => server.CompanyId == company.Id).ToList();

        var tier1WorkstationAdmins = FindGroup(groups, "GG Tier1 Workstation Admins");
        var tier1ServerAdmins = FindGroup(groups, "GG Tier1 Server Admins");
        var tier2Helpdesk = FindGroup(groups, "GG Tier2 Helpdesk");
        var tier0PawUsers = FindGroup(groups, "GG Tier0 PAW Users");
        var tier1PawUsers = FindGroup(groups, "GG Tier1 PAW Users");
        var tier0PawDevices = FindGroup(groups, "GG Tier0 PAW Devices");
        var tier1PawDevices = FindGroup(groups, "GG Tier1 PAW Devices");
        var tier1ManagedWorkstations = FindGroup(groups, "GG Tier1 Managed Workstations");
        var tier1ManagedServers = FindGroup(groups, "GG Tier1 Managed Servers");
        var mspOperators = FindGroup(groups, "GG MSP Operations");
        var addedHelpdeskAssignment = false;
        var addedManagedServiceAssignment = false;

        foreach (var device in devices)
        {
            if (string.Equals(device.DeviceType, "Workstation", StringComparison.OrdinalIgnoreCase)
                && tier1ManagedWorkstations is not null)
            {
                AddComputerMembership(world, tier1ManagedWorkstations.Id, device.Id, "Device");
            }

            if (string.Equals(device.DeviceType, "PrivilegedAccessWorkstation", StringComparison.OrdinalIgnoreCase))
            {
                var privilegedAccount = privilegedAccounts.FirstOrDefault(account =>
                    !string.IsNullOrWhiteSpace(device.AssignedPersonId)
                    && string.Equals(account.PersonId, device.AssignedPersonId, StringComparison.OrdinalIgnoreCase));
                var deviceTierGroup = string.Equals(privilegedAccount?.AdministrativeTier, "Tier0", StringComparison.OrdinalIgnoreCase)
                    ? tier0PawDevices
                    : tier1PawDevices;
                var adminPrincipalGroup = string.Equals(privilegedAccount?.AdministrativeTier, "Tier0", StringComparison.OrdinalIgnoreCase)
                    ? tier0PawUsers
                    : tier1PawUsers;
                var tier = privilegedAccount?.AdministrativeTier ?? "Tier1";

                if (deviceTierGroup is not null)
                {
                    AddComputerMembership(world, deviceTierGroup.Id, device.Id, "Device");
                }

                if (adminPrincipalGroup is not null)
                {
                    world.EndpointAdministrativeAssignments.Add(new EndpointAdministrativeAssignment
                    {
                        Id = _idFactory.Next("EAA"),
                        CompanyId = company.Id,
                        EndpointType = "Device",
                        EndpointId = device.Id,
                        PrincipalObjectId = adminPrincipalGroup.Id,
                        PrincipalType = "Group",
                        AccessRole = "LocalAdministrator",
                        AdministrativeTier = tier,
                        AssignmentScope = "Dedicated",
                        ManagementPlane = "DirectoryPolicy"
                    });
                }
            }
            else
            {
                if (tier1WorkstationAdmins is not null)
                {
                    world.EndpointAdministrativeAssignments.Add(new EndpointAdministrativeAssignment
                    {
                        Id = _idFactory.Next("EAA"),
                        CompanyId = company.Id,
                        EndpointType = "Device",
                        EndpointId = device.Id,
                        PrincipalObjectId = tier1WorkstationAdmins.Id,
                        PrincipalType = "Group",
                        AccessRole = "LocalAdministrator",
                        AdministrativeTier = "Tier1",
                        AssignmentScope = "Persistent",
                        ManagementPlane = "DirectoryPolicy"
                    });
                }

                if (tier2Helpdesk is not null && (!addedHelpdeskAssignment || _randomSource.NextDouble() < 0.35))
                {
                    world.EndpointAdministrativeAssignments.Add(new EndpointAdministrativeAssignment
                    {
                        Id = _idFactory.Next("EAA"),
                        CompanyId = company.Id,
                        EndpointType = "Device",
                        EndpointId = device.Id,
                        PrincipalObjectId = tier2Helpdesk.Id,
                        PrincipalType = "Group",
                        AccessRole = "SupportAdministrator",
                        AdministrativeTier = "Tier2",
                        AssignmentScope = "JustInTimeEligible",
                        ManagementPlane = "EndpointManagement"
                    });
                    addedHelpdeskAssignment = true;
                }
            }
        }

        foreach (var server in servers)
        {
            if (tier1ManagedServers is not null)
            {
                AddComputerMembership(world, tier1ManagedServers.Id, server.Id, "Server");
            }

            if (tier1ServerAdmins is not null)
            {
                world.EndpointAdministrativeAssignments.Add(new EndpointAdministrativeAssignment
                {
                    Id = _idFactory.Next("EAA"),
                    CompanyId = company.Id,
                    EndpointType = "Server",
                    EndpointId = server.Id,
                    PrincipalObjectId = tier1ServerAdmins.Id,
                    PrincipalType = "Group",
                    AccessRole = "LocalAdministrator",
                    AdministrativeTier = "Tier1",
                    AssignmentScope = "Persistent",
                    ManagementPlane = "DirectoryPolicy"
                });
            }

            if (mspOperators is not null && (!addedManagedServiceAssignment || _randomSource.NextDouble() < 0.25))
            {
                world.EndpointAdministrativeAssignments.Add(new EndpointAdministrativeAssignment
                {
                    Id = _idFactory.Next("EAA"),
                    CompanyId = company.Id,
                    EndpointType = "Server",
                    EndpointId = server.Id,
                    PrincipalObjectId = mspOperators.Id,
                    PrincipalType = "Group",
                    AccessRole = "OperationsAdministrator",
                    AdministrativeTier = "Tier1",
                    AssignmentScope = "ManagedService",
                    ManagementPlane = "PrivilegedAccessManagement"
                });
                addedManagedServiceAssignment = true;
            }
        }
    }

    private void AddComputerMembership(SyntheticEnterpriseWorld world, string groupId, string memberObjectId, string memberObjectType)
    {
        var resolvedMemberType = memberObjectType;
        var resolvedMemberId = memberObjectId;

        if (string.Equals(memberObjectType, "Device", StringComparison.OrdinalIgnoreCase))
        {
            var device = world.Devices.FirstOrDefault(candidate => string.Equals(candidate.Id, memberObjectId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(device?.OnPremDirectoryAccountId))
            {
                resolvedMemberType = "Account";
                resolvedMemberId = device.OnPremDirectoryAccountId!;
            }
            else if (!string.IsNullOrWhiteSpace(device?.CloudDirectoryAccountId))
            {
                resolvedMemberType = "Account";
                resolvedMemberId = device.CloudDirectoryAccountId!;
            }
            else if (!string.IsNullOrWhiteSpace(device?.DirectoryAccountId))
            {
                resolvedMemberType = "Account";
                resolvedMemberId = device.DirectoryAccountId!;
            }
            else
            {
                return;
            }
        }
        else if (string.Equals(memberObjectType, "Server", StringComparison.OrdinalIgnoreCase))
        {
            var server = world.Servers.FirstOrDefault(candidate => string.Equals(candidate.Id, memberObjectId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(server?.OnPremDirectoryAccountId))
            {
                resolvedMemberType = "Account";
                resolvedMemberId = server.OnPremDirectoryAccountId!;
            }
            else if (!string.IsNullOrWhiteSpace(server?.CloudDirectoryAccountId))
            {
                resolvedMemberType = "Account";
                resolvedMemberId = server.CloudDirectoryAccountId!;
            }
            else if (!string.IsNullOrWhiteSpace(server?.DirectoryAccountId))
            {
                resolvedMemberType = "Account";
                resolvedMemberId = server.DirectoryAccountId!;
            }
            else
            {
                return;
            }
        }

        if (world.GroupMemberships.Any(membership =>
                string.Equals(membership.GroupId, groupId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(membership.MemberObjectId, resolvedMemberId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(membership.MemberObjectType, resolvedMemberType, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        world.GroupMemberships.Add(new DirectoryGroupMembership
        {
            Id = _idFactory.Next("MEM"),
            GroupId = groupId,
            MemberObjectId = resolvedMemberId,
            MemberObjectType = resolvedMemberType
        });
    }

    private MachineAccountLinks CreateMachineAccounts(
        SyntheticEnterpriseWorld world,
        Company company,
        string hostname,
        DirectoryOrganizationalUnit? onPremOu,
        string? administrativeTier,
        bool createOnPremAccount,
        bool createCloudAccount)
    {
        string? onPremAccountId = null;
        string? cloudAccountId = null;
        var existingSamNames = world.Accounts
            .Select(account => account.SamAccountName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingUpns = world.Accounts
            .Select(account => account.UserPrincipalName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (createOnPremAccount)
        {
            var sam = BuildUniqueMachineSamAccountName(hostname, existingSamNames, appendDollarSign: true);
            var upn = BuildUniqueMachineUpn(hostname, company.PrimaryDomain, existingUpns, appendDollarSign: true);
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(1, 30));
            var account = new DirectoryAccount
            {
                Id = BuildMachineAccountId(company.Name, hostname, "AD"),
                CompanyId = company.Id,
                AccountType = "Device",
                SamAccountName = sam,
                UserPrincipalName = upn,
                Mail = null,
                DistinguishedName = onPremOu is null ? $"CN={EscapeDn(hostname)}" : $"CN={EscapeDn(hostname)},{onPremOu.DistinguishedName}",
                OuId = onPremOu?.Id ?? string.Empty,
                Enabled = true,
                Privileged = false,
                MfaEnabled = false,
                GeneratedPassword = CreatePassword(24),
                PasswordProfile = "MachineManaged",
                AdministrativeTier = administrativeTier,
                PasswordLastSet = passwordLastSet,
                PasswordExpires = null,
                PasswordNeverExpires = true,
                MustChangePasswordAtNextLogon = false,
                UserType = "Member",
                IdentityProvider = "HybridDirectory",
                ExternalAccessCategory = "Device"
            };

            world.Accounts.Add(account);
            onPremAccountId = account.Id;
        }

        if (createCloudAccount)
        {
            var sam = BuildUniqueMachineSamAccountName(hostname, existingSamNames, appendDollarSign: false);
            var upn = BuildUniqueMachineUpn(hostname, company.PrimaryDomain, existingUpns, appendDollarSign: false);
            var passwordLastSet = _clock.UtcNow.AddDays(-_randomSource.Next(1, 30));
            var account = new DirectoryAccount
            {
                Id = BuildMachineAccountId(company.Name, hostname, "ENTRA"),
                CompanyId = company.Id,
                AccountType = "Device",
                SamAccountName = sam,
                UserPrincipalName = upn,
                Mail = null,
                DistinguishedName = hostname,
                OuId = string.Empty,
                Enabled = true,
                Privileged = false,
                MfaEnabled = false,
                GeneratedPassword = CreatePassword(24),
                PasswordProfile = "CloudDeviceManaged",
                AdministrativeTier = administrativeTier,
                PasswordLastSet = passwordLastSet,
                PasswordExpires = null,
                PasswordNeverExpires = true,
                MustChangePasswordAtNextLogon = false,
                UserType = "Member",
                IdentityProvider = "EntraID",
                ExternalAccessCategory = "Device"
            };

            world.Accounts.Add(account);
            cloudAccountId = account.Id;
        }

        return new MachineAccountLinks(onPremAccountId ?? cloudAccountId, onPremAccountId, cloudAccountId);
    }

    private JoinProfile ResolveWorkstationJoinProfile(string operatingSystem, bool contextSupportsHybridDirectory)
    {
        if (operatingSystem.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return contextSupportsHybridDirectory
                ? new JoinProfile(CreateOnPremAccount: true, CreateCloudAccount: true)
                : new JoinProfile(CreateOnPremAccount: true, CreateCloudAccount: false);
        }

        return contextSupportsHybridDirectory
            ? new JoinProfile(CreateOnPremAccount: false, CreateCloudAccount: true)
            : new JoinProfile(CreateOnPremAccount: false, CreateCloudAccount: false);
    }

    private static string BuildUniqueMachineSamAccountName(string hostname, ISet<string> existingSamNames, bool appendDollarSign)
    {
        var sanitized = hostname.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        var stemBudget = appendDollarSign ? 15 : 20;
        var stem = sanitized[..Math.Min(stemBudget, sanitized.Length)];
        var candidate = appendDollarSign ? $"{stem}$" : stem;
        var suffix = 1;

        while (!existingSamNames.Add(candidate))
        {
            var suffixText = suffix.ToString("00");
            var adjustedStemBudget = Math.Max(1, stemBudget - suffixText.Length);
            stem = sanitized[..Math.Min(adjustedStemBudget, sanitized.Length)];
            candidate = appendDollarSign ? $"{stem}{suffixText}$" : $"{stem}{suffixText}";
            suffix++;
        }

        return candidate;
    }

    private static string BuildUniqueMachineUpn(string hostname, string domain, ISet<string> existingUpns, bool appendDollarSign)
    {
        var localPart = appendDollarSign ? $"{hostname}$" : hostname;
        var candidate = $"{localPart.ToLowerInvariant()}@{domain}";
        var suffix = 1;

        while (!existingUpns.Add(candidate))
        {
            candidate = $"{hostname.ToLowerInvariant()}{suffix:00}{(appendDollarSign ? "$" : string.Empty)}@{domain}";
            suffix++;
        }

        return candidate;
    }

    private static string BuildMachineAccountId(string companyName, string hostname, string providerCode)
    {
        var company = Slug(companyName);
        var host = Slug(hostname);
        var payload = $"{companyName.ToUpperInvariant()}|{hostname.ToUpperInvariant()}|{providerCode.ToUpperInvariant()}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var suffix = Convert.ToHexString(digest[..6]);
        return $"ACT-{company[..Math.Min(6, company.Length)]}-{host[..Math.Min(8, host.Length)]}-{providerCode}-{suffix}".ToUpperInvariant();
    }

    private string CreatePassword(int length)
    {
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string digits = "0123456789";
        const string symbols = "!@#$%^&*()_-+=[]{}";
        var all = lower + upper + digits + symbols;
        var buffer = new char[length];
        buffer[0] = lower[_randomSource.Next(lower.Length)];
        buffer[1] = upper[_randomSource.Next(upper.Length)];
        buffer[2] = digits[_randomSource.Next(digits.Length)];
        buffer[3] = symbols[_randomSource.Next(symbols.Length)];

        for (var i = 4; i < buffer.Length; i++)
        {
            buffer[i] = all[_randomSource.Next(all.Length)];
        }

        for (var i = buffer.Length - 1; i > 0; i--)
        {
            var swapIndex = _randomSource.Next(i + 1);
            (buffer[i], buffer[swapIndex]) = (buffer[swapIndex], buffer[i]);
        }

        return new string(buffer);
    }

    private static string EscapeDn(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal);

    private static DirectoryGroup? FindGroup(IReadOnlyList<DirectoryGroup> groups, string name)
        => groups.FirstOrDefault(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));

    private void CreateEndpointPolicyAndLocalGroupState(SyntheticEnterpriseWorld world, Company company)
    {
        foreach (var device in world.Devices.Where(device => device.CompanyId == company.Id))
        {
            var administrativeTier = ResolveEndpointTier(device.DeviceType);
            AddPolicyBaseline(world, company.Id, "Device", device.Id, "Endpoint Security Baseline", "SecurityBaseline", "Intune", "Applied", "Applied", administrativeTier);
            AddPolicyBaseline(world, company.Id, "Device", device.Id, "BitLocker Disk Encryption", "Encryption", "Intune", "Enabled", "Enabled", administrativeTier);
            AddPolicyBaseline(world, company.Id, "Device", device.Id, "Windows LAPS Rotation", "CredentialManagement", "Intune", "Enabled", "Enabled", administrativeTier);
            AddPolicyBaseline(world, company.Id, "Device", device.Id, "Screen Lock Timeout", "UserExperience", "Intune", "15Minutes", "15Minutes", administrativeTier);

            if (string.Equals(device.DeviceType, "PrivilegedAccessWorkstation", StringComparison.OrdinalIgnoreCase))
            {
                AddPolicyBaseline(world, company.Id, "Device", device.Id, "Credential Guard", "IdentityProtection", "Intune", "Enabled", "Enabled", administrativeTier);
                AddPolicyBaseline(world, company.Id, "Device", device.Id, "Restricted Internet Access", "NetworkIsolation", "Intune", "Enabled", "Enabled", administrativeTier);
            }
        }

        foreach (var server in world.Servers.Where(server => server.CompanyId == company.Id))
        {
            AddPolicyBaseline(world, company.Id, "Server", server.Id, "Server Security Baseline", "SecurityBaseline", "GroupPolicy", "Applied", "Applied", "Tier1");
            AddPolicyBaseline(world, company.Id, "Server", server.Id, "Windows Firewall", "NetworkProtection", "GroupPolicy", "Enabled", "Enabled", "Tier1");
            AddPolicyBaseline(world, company.Id, "Server", server.Id, "Patch Maintenance Window", "Operations", "ConfigurationManager", "Scheduled", "Scheduled", "Tier1");

            if (string.Equals(server.ServerRole, "Domain Controller", StringComparison.OrdinalIgnoreCase))
            {
                AddPolicyBaseline(world, company.Id, "Server", server.Id, "Privileged Access Restrictions", "IdentityProtection", "GroupPolicy", "Hardened", "Hardened", "Tier0");
            }
        }

        foreach (var assignment in world.EndpointAdministrativeAssignments.Where(assignment => assignment.CompanyId == company.Id))
        {
            AddLocalGroupMember(world, assignment, "Administrators");

            if (string.Equals(assignment.EndpointType, "Server", StringComparison.OrdinalIgnoreCase)
                && assignment.AccessRole is "LocalAdministrator" or "OperationsAdministrator")
            {
                AddLocalGroupMember(world, assignment, "Remote Desktop Users");
            }
        }

    }

    private void AddPolicyBaseline(
        SyntheticEnterpriseWorld world,
        string companyId,
        string endpointType,
        string endpointId,
        string policyName,
        string policyCategory,
        string assignedFrom,
        string desiredState,
        string currentState,
        string administrativeTier)
    {
        world.EndpointPolicyBaselines.Add(new EndpointPolicyBaseline
        {
            Id = _idFactory.Next("EPB"),
            CompanyId = companyId,
            EndpointType = endpointType,
            EndpointId = endpointId,
            PolicyName = policyName,
            PolicyCategory = policyCategory,
            AssignedFrom = assignedFrom,
            EnforcementMode = "Enforced",
            DesiredState = desiredState,
            CurrentState = currentState,
            AdministrativeTier = administrativeTier
        });
    }

    private void AddLocalGroupMember(SyntheticEnterpriseWorld world, EndpointAdministrativeAssignment assignment, string localGroupName)
    {
        var principalName = ResolvePrincipalName(world, assignment.PrincipalType, assignment.PrincipalObjectId);
        world.EndpointLocalGroupMembers.Add(new EndpointLocalGroupMember
        {
            Id = _idFactory.Next("ELG"),
            CompanyId = assignment.CompanyId,
            EndpointType = assignment.EndpointType,
            EndpointId = assignment.EndpointId,
            LocalGroupName = localGroupName,
            PrincipalObjectId = assignment.PrincipalObjectId,
            PrincipalType = assignment.PrincipalType,
            PrincipalName = principalName,
            MembershipSource = assignment.ManagementPlane,
            AdministrativeTier = assignment.AdministrativeTier
        });
    }

    private static string ResolvePrincipalName(SyntheticEnterpriseWorld world, string principalType, string principalObjectId)
    {
        if (string.Equals(principalType, "Account", StringComparison.OrdinalIgnoreCase))
        {
            return world.Accounts.FirstOrDefault(account => string.Equals(account.Id, principalObjectId, StringComparison.OrdinalIgnoreCase))?.UserPrincipalName
                   ?? principalObjectId;
        }

        return world.Groups.FirstOrDefault(group => string.Equals(group.Id, principalObjectId, StringComparison.OrdinalIgnoreCase))?.Name
               ?? principalObjectId;
    }

    private static string ResolveEndpointTier(string deviceType)
        => string.Equals(deviceType, "PrivilegedAccessWorkstation", StringComparison.OrdinalIgnoreCase)
            ? "Tier1"
            : "Tier2";

    private static string BuildHostname(string companyName, string seed, int index, string prefix)
    {
        var company = Slug(companyName);
        return $"{prefix}-{company[..Math.Min(6, company.Length)]}-{index:000}".ToUpperInvariant();
    }

    private static string BuildServerHostname(string companyName, string role, int index)
    {
        var company = Slug(companyName);
        var roleCode = role switch
        {
            "Domain Controller" => "DC",
            "File Server" => "FS",
            "SQL Server" => "SQL",
            "Web Server" => "WEB",
            "Application Server" => "APP",
            "Jump Host" => "JMP",
            "Management Server" => "MGT",
            "Remote Access Server" => "RAS",
            "Print Server" => "PRN",
            _ => Slug(role)[..Math.Min(4, Slug(role).Length)].ToUpperInvariant()
        };

        return $"SRV-{company[..Math.Min(6, company.Length)]}-{roleCode}-{index:000}".ToUpperInvariant();
    }

    private static string BuildNetworkHostname(string companyName, string city, int index, string prefix)
    {
        var company = Slug(companyName);
        var location = Slug(city);
        return $"{prefix}-{company[..Math.Min(6, company.Length)]}-{location[..Math.Min(6, location.Length)]}-{index:000}".ToUpperInvariant();
    }

    private static string Slug(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private sealed record NetworkAssetProfile(string AssetType, string Vendor, string Model, string HostPrefix);
    private sealed record JoinProfile(bool CreateOnPremAccount, bool CreateCloudAccount);
    private sealed record MachineAccountLinks(string? PrimaryAccountId, string? OnPremAccountId, string? CloudAccountId);

    private static string Read(Dictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value ?? "" : "";
}
