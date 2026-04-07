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
            var accounts = world.Accounts.Where(a => a.CompanyId == company.Id && a.AccountType == "User").ToList();

            CreateWorkstations(world, company, definition, people, accounts);
            CreateServers(world, company, definition, offices, teams);
            CreateNetworkAssets(world, company, definition, offices);
            CreateTelephonyAssets(world, company, definition, people, offices);
            CreateSoftwareInstallations(world, company);
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
        IReadOnlyList<DirectoryAccount> accounts)
    {
        var target = Math.Max(1, (int)Math.Round(people.Count * Math.Clamp(definition.WorkstationCoverageRatio, 0.1, 1.5)));
        var models = new[]
        {
            ("Dell", "Latitude 7450", "Windows 11 Enterprise", "23H2"),
            ("Lenovo", "ThinkPad T14", "Windows 11 Enterprise", "23H2"),
            ("HP", "EliteBook 840", "Windows 11 Enterprise", "23H2"),
            ("Apple", "MacBook Pro 14", "macOS", "14.7")
        };

        for (var i = 0; i < target; i++)
        {
            var person = people[i % people.Count];
            var account = accounts.FirstOrDefault(a => a.PersonId == person.Id);
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
                DomainJoined = model.Item3.StartsWith("Windows", StringComparison.OrdinalIgnoreCase),
                ComplianceState = _randomSource.NextDouble() < 0.92 ? "Compliant" : "NonCompliant",
                LastSeen = _clock.UtcNow.AddDays(-_randomSource.Next(0, 45))
            });
        }
    }

    private void CreateServers(
        SyntheticEnterpriseWorld world,
        Company company,
        ScenarioCompanyDefinition definition,
        IReadOnlyList<Office> offices,
        IReadOnlyList<Team> teams)
    {
        var roles = new[] { "Domain Controller", "File Server", "SQL Server", "Web Server", "Application Server", "Jump Host", "Print Server" };
        var envs = new[] { "Production", "Production", "Production", "Staging", "Development" };

        for (var i = 0; i < Math.Max(1, definition.ServerCount); i++)
        {
            var office = offices.Count > 0 ? offices[i % offices.Count] : null;
            var team = teams.Count > 0 ? teams[i % teams.Count] : null;

            world.Servers.Add(new ServerAsset
            {
                Id = _idFactory.Next("SRV"),
                CompanyId = company.Id,
                Hostname = BuildHostname(company.Name, roles[i % roles.Length].Replace(" ", ""), i + 1, "SRV"),
                ServerRole = roles[i % roles.Length],
                Environment = envs[i % envs.Length],
                OperatingSystem = "Windows Server",
                OperatingSystemVersion = i % 3 == 0 ? "2022" : "2019",
                OfficeId = office?.Id ?? "",
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
            var person = people.Count > 0 and not false ? people[i % people.Count] : null;
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
