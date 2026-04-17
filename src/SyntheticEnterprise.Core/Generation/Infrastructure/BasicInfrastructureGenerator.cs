namespace SyntheticEnterprise.Core.Generation.Infrastructure;

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
            var userAccounts = world.Accounts.Where(a => a.CompanyId == company.Id && a.AccountType == "User").ToList();
            var privilegedAccounts = world.Accounts.Where(a => a.CompanyId == company.Id && a.AccountType == "Privileged").ToList();
            var ous = world.OrganizationalUnits.Where(o => o.CompanyId == company.Id).ToList();

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
                OuId = workstationOu?.Id,
                DistinguishedName = workstationOu is null ? null : $"CN={hostname},{workstationOu.DistinguishedName}",
                DomainJoined = model.Item3.StartsWith("Windows", StringComparison.OrdinalIgnoreCase),
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
            .Take(Math.Max(1, privilegedAccounts.Count / 2))
            .ToList();

        for (var i = 0; i < privilegedPeople.Count; i++)
        {
            var privilegedPerson = privilegedPeople[i].Person!;
            var privilegedAccount = privilegedPeople[i].Account;
            var hostname = BuildHostname(company.Name, privilegedPerson.LastName, i + 1, "PAW");

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
        var roles = new[] { "Domain Controller", "File Server", "SQL Server", "Web Server", "Application Server", "Jump Host", "Print Server" };
        var envs = new[] { "Production", "Production", "Production", "Staging", "Development" };
        var serverEnvironments = ous
            .Where(ou => ou.ParentOuId is not null)
            .GroupBy(ou => ou.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var serversOu = ous.FirstOrDefault(ou => ou.Name == "Servers");

        for (var i = 0; i < Math.Max(1, definition.ServerCount); i++)
        {
            var office = offices.Count > 0 ? offices[i % offices.Count] : null;
            var team = teams.Count > 0 ? teams[i % teams.Count] : null;
            var environment = envs[i % envs.Length];
            var targetOu = serverEnvironments.TryGetValue(environment, out var environmentOu)
                ? environmentOu
                : serversOu;
            var hostname = BuildHostname(company.Name, roles[i % roles.Length].Replace(" ", ""), i + 1, "SRV");

            world.Servers.Add(new ServerAsset
            {
                Id = _idFactory.Next("SRV"),
                CompanyId = company.Id,
                Hostname = hostname,
                ServerRole = roles[i % roles.Length],
                Environment = environment,
                OperatingSystem = "Windows Server",
                OperatingSystemVersion = i % 3 == 0 ? "2022" : "2019",
                OfficeId = office?.Id ?? "",
                OuId = targetOu?.Id,
                DistinguishedName = targetOu is null ? null : $"CN={hostname},{targetOu.DistinguishedName}",
                DomainJoined = true,
                OwnerTeamId = team?.Id ?? "",
                Criticality = i % 5 == 0 ? "High" : "Medium"
            });
        }
    }

    private void CreateNetworkAssets(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Office> offices)
    {
        var assetTypes = new[] { "Switch", "Router", "Firewall", "Wireless Controller", "Access Point", "Load Balancer" };
        var vendors = new[] { ("Cisco", "Catalyst"), ("Palo Alto", "PA-Series"), ("Fortinet", "FortiGate"), ("Aruba", "CX") };

        foreach (var office in offices)
        {
            for (var i = 0; i < Math.Max(1, definition.NetworkAssetCountPerOffice); i++)
            {
                var vendor = vendors[i % vendors.Length];
                world.NetworkAssets.Add(new NetworkAsset
                {
                    Id = _idFactory.Next("NET"),
                    CompanyId = company.Id,
                    AssetType = assetTypes[i % assetTypes.Length],
                    Hostname = BuildHostname(company.Name, office.City, i + 1, "NET"),
                    OfficeId = office.Id,
                    Vendor = vendor.Item1,
                    Model = vendor.Item2
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

        var tier1WorkstationAdmins = FindGroup(groups, "SG-Tier1-WorkstationAdmins");
        var tier1ServerAdmins = FindGroup(groups, "SG-Tier1-ServerAdmins");
        var tier2Helpdesk = FindGroup(groups, "SG-Tier2-Helpdesk");
        var tier0PawUsers = FindGroup(groups, "SG-Tier0-PAW-Users");
        var tier1PawUsers = FindGroup(groups, "SG-Tier1-PAW-Users");
        var tier0PawDevices = FindGroup(groups, "SG-Tier0-PAW-Devices");
        var tier1PawDevices = FindGroup(groups, "SG-Tier1-PAW-Devices");
        var tier1ManagedWorkstations = FindGroup(groups, "SG-Tier1-ManagedWorkstations");
        var tier1ManagedServers = FindGroup(groups, "SG-Tier1-ManagedServers");
        var mspOperators = FindGroup(groups, "SG-MSP-Operators");
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
                var privilegedAccount = privilegedAccounts.FirstOrDefault(account => account.Id == device.DirectoryAccountId);
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
        if (world.GroupMemberships.Any(membership =>
                string.Equals(membership.GroupId, groupId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(membership.MemberObjectId, memberObjectId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(membership.MemberObjectType, memberObjectType, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        world.GroupMemberships.Add(new DirectoryGroupMembership
        {
            Id = _idFactory.Next("MEM"),
            GroupId = groupId,
            MemberObjectId = memberObjectId,
            MemberObjectType = memberObjectType
        });
    }

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

        foreach (var device in world.Devices.Where(device => device.CompanyId == company.Id))
        {
            AddBuiltInLocalGroupMember(world, company.Id, "Device", device.Id, "Administrators", "BUILTIN\\Administrator", "BuiltIn", ResolveEndpointTier(device.DeviceType));
        }

        foreach (var server in world.Servers.Where(server => server.CompanyId == company.Id))
        {
            AddBuiltInLocalGroupMember(world, company.Id, "Server", server.Id, "Administrators", "BUILTIN\\Administrator", "BuiltIn", "Tier1");
            AddBuiltInLocalGroupMember(world, company.Id, "Server", server.Id, "Remote Desktop Users", "BUILTIN\\Remote Desktop Users", "BuiltIn", "Tier1");
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

    private void AddBuiltInLocalGroupMember(
        SyntheticEnterpriseWorld world,
        string companyId,
        string endpointType,
        string endpointId,
        string localGroupName,
        string principalName,
        string membershipSource,
        string administrativeTier)
    {
        world.EndpointLocalGroupMembers.Add(new EndpointLocalGroupMember
        {
            Id = _idFactory.Next("ELG"),
            CompanyId = companyId,
            EndpointType = endpointType,
            EndpointId = endpointId,
            LocalGroupName = localGroupName,
            PrincipalObjectId = null,
            PrincipalType = "BuiltIn",
            PrincipalName = principalName,
            MembershipSource = membershipSource,
            AdministrativeTier = administrativeTier
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
        var part = Slug(seed);
        return $"{prefix}-{company[..Math.Min(6, company.Length)]}-{part[..Math.Min(6, part.Length)]}-{index:000}".ToUpperInvariant();
    }

    private static string Slug(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string Read(Dictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value ?? "" : "";
}
