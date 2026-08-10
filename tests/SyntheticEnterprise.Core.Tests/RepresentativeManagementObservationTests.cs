using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Generation.Infrastructure;

namespace SyntheticEnterprise.Core.Tests;

public sealed class RepresentativeManagementObservationTests
{
    private static readonly string[] ForbiddenScenarioTerms =
    [
        "cartograph",
        "duckburg",
        "default-engagement",
        "rmp-",
        "nd-",
        "cat-",
        "node-applications-retry",
        "node-catalog",
        "explicit-exclusion",
        "relationship-research",
        "server-attested",
        "server-no-pointer",
    ];

    [Fact]
    public void WorldGenerator_MaterializesNeutralRepresentativeManagementFacts_WhenEnabled()
    {
        var result = Generate(includeRepresentativeFacts: true);
        var world = result.World;

        var subjects = world.ManagementObservations
            .Select(observation => observation.EndpointId)
            .ToHashSet(StringComparer.Ordinal);
        var representativeDevices = world.Devices
            .Where(device => subjects.Contains(device.Id))
            .ToArray();
        var representativeServers = world.Servers
            .Where(server => subjects.Contains(server.Id))
            .ToArray();

        Assert.True(world.ManagementObservations.Count >= 12);
        Assert.True(representativeDevices.Length >= 8);
        Assert.True(representativeServers.Length >= 4);
        Assert.Contains(world.ManagementObservations, observation =>
            observation.RegistrationState == "Registered"
            && observation.LastCheckInAtUtc.HasValue);
        Assert.Contains(world.ManagementObservations, observation =>
            observation.RegistrationState == "Unreachable"
            && observation.LastCheckInAtUtc < observation.ObservedAtUtc.AddDays(-60));
        Assert.Contains(world.ManagementObservations, observation =>
            observation.RegistrationState == "NotRegistered"
            && observation.ManagementProvider == "None");
        Assert.Contains(world.ManagementObservations, observation =>
            observation.JoinState == "HybridJoined");
        Assert.Contains(world.ManagementObservations, observation =>
            observation.JoinState == "Workgroup");
        Assert.True(world.ManagementObservations
            .Select(observation => observation.Cohort)
            .Distinct(StringComparer.Ordinal)
            .Count() >= 3);

        Assert.All(representativeDevices.Where(device => device.DomainJoined), device =>
            Assert.False(string.IsNullOrWhiteSpace(device.DirectoryAccountId)));
        Assert.Contains(representativeServers, server => !string.IsNullOrWhiteSpace(server.OwnerTeamId));
        Assert.Contains(representativeServers, server => string.IsNullOrWhiteSpace(server.OwnerTeamId));

        Assert.Contains(world.DeviceSoftwareInstallations, installation =>
            subjects.Contains(installation.DeviceId));
        Assert.Contains(world.ServerSoftwareInstallations, installation =>
            subjects.Contains(installation.ServerId));
        var applicationsById = world.Applications.ToDictionary(application => application.Id, StringComparer.Ordinal);
        var serversById = world.Servers.ToDictionary(server => server.Id, StringComparer.Ordinal);
        var peopleById = world.People.ToDictionary(person => person.Id, StringComparer.Ordinal);
        var servicesById = world.ApplicationServices.ToDictionary(service => service.Id, StringComparer.Ordinal);
        var serverSoftwareIds = world.ServerSoftwareInstallations
            .Select(installation => installation.ServerId)
            .ToHashSet(StringComparer.Ordinal);

        var installedOnHistory = world.RelationshipHistoryObservations
            .Where(observation => observation.RelationshipType == "InstalledOn")
            .ToArray();
        Assert.Contains(installedOnHistory, observation => observation.LifecycleState == "Active");
        Assert.Contains(installedOnHistory, observation => observation.LifecycleState == "Removed");
        Assert.All(installedOnHistory, observation =>
        {
            Assert.Equal("Application", observation.FromEntityType);
            Assert.Equal("Server", observation.ToEntityType);
            var application = applicationsById[observation.FromEntityId];
            var server = serversById[observation.ToEntityId];
            Assert.Equal(server.CompanyId, application.CompanyId);
            Assert.Equal(server.CompanyId, observation.CompanyId);
            Assert.Contains(server.Id, serverSoftwareIds);
            Assert.Contains(world.ApplicationServiceHostings, hosting =>
                hosting.CompanyId == application.CompanyId
                && hosting.HostType == "Server"
                && hosting.HostId == server.Id
                && servicesById[hosting.ApplicationServiceId].ApplicationId == application.Id);
        });

        var ownershipHistory = world.RelationshipHistoryObservations
            .Where(observation => observation.RelationshipType == "Owns")
            .ToArray();
        Assert.Contains(ownershipHistory, observation => observation.LifecycleState == "Active");
        Assert.Contains(ownershipHistory, observation => observation.LifecycleState == "Removed");
        Assert.All(ownershipHistory, observation =>
        {
            Assert.Equal("Person", observation.FromEntityType);
            Assert.Equal("Application", observation.ToEntityType);
            var owner = peopleById[observation.FromEntityId];
            var application = applicationsById[observation.ToEntityId];
            Assert.Equal(application.CompanyId, owner.CompanyId);
            Assert.Equal(application.CompanyId, observation.CompanyId);
            Assert.Equal(application.OwnerDepartmentId, owner.DepartmentId);
        });

        var sourceText = string.Join('|',
            world.ManagementObservations.SelectMany(observation => new[]
            {
                observation.ObservationKind,
                observation.ManagementProvider,
                observation.RegistrationState,
                observation.Cohort,
            })
            .Concat(representativeDevices.Select(device => device.Hostname))
            .Concat(representativeServers.Select(server => server.Hostname))
            .Concat(world.RelationshipHistoryObservations.Select(observation => observation.RelationshipType)));
        Assert.DoesNotContain("RMP-", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ND-", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expected answer", sourceText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorldGenerator_UsesGenericDistributionsWithoutScenarioKeysOrConclusions()
    {
        var world = Generate(includeRepresentativeFacts: true).World;
        var endpointIds = world.ManagementObservations
            .Select(observation => observation.EndpointId)
            .ToHashSet(StringComparer.Ordinal);
        var generatedText = string.Join('|',
            world.ManagementObservations.SelectMany(observation => new[]
            {
                observation.ObservationKind,
                observation.SourceKind,
                observation.ManagementProvider,
                observation.RegistrationState,
                observation.JoinState,
                observation.Cohort,
            })
            .Concat(world.Devices.Where(device => endpointIds.Contains(device.Id)).Select(device => device.Hostname))
            .Concat(world.Servers.Where(server => endpointIds.Contains(server.Id)).Select(server => server.Hostname)));

        Assert.All(ForbiddenScenarioTerms, term =>
            Assert.DoesNotContain(term, generatedText, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotMatch(@"LAB-(WIN|SERVER|ND)-", generatedText);
    }

    [Fact]
    public void ProductionSource_DoesNotContainProductScenarioVocabularyOrFixedFixtureHostnames()
    {
        var sourceRoot = FindSourceRoot();
        var sourceText = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        Assert.DoesNotContain("Cartograph", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Duckburg", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("default-engagement", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?i)\b(?:RMP|ND|CAT)-\d{2}\b", sourceText);
        Assert.DoesNotMatch(@"(?i)\bLAB-(?:WIN|SERVER|ND)-", sourceText);
        Assert.DoesNotContain("node-applications-retry", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("node-catalog", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explicit-exclusion", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("relationship-research", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server-attested", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server-no-pointer", sourceText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndpointManagementObservation_RepresentsProviderNeutralHostedGuestDeploymentCapability()
    {
        var observationType = typeof(SyntheticEnterprise.Contracts.Models.EndpointManagementObservation);

        Assert.NotNull(observationType.GetProperty("HostingEnvironmentKind"));
        Assert.NotNull(observationType.GetProperty("HostingProvider"));
        Assert.NotNull(observationType.GetProperty("OutOfBandGuestDeploymentCapability"));

        var world = Generate(includeRepresentativeFacts: true).World;
        Assert.Contains(world.ManagementObservations, observation =>
            string.Equals(
                observationType.GetProperty("HostingEnvironmentKind")!.GetValue(observation) as string,
                "HostedCompute",
                StringComparison.Ordinal)
            && string.Equals(
                observationType.GetProperty("HostingProvider")!.GetValue(observation) as string,
                "Azure",
                StringComparison.Ordinal)
            && string.Equals(
                observationType.GetProperty("OutOfBandGuestDeploymentCapability")!.GetValue(observation) as string,
                "Supported",
                StringComparison.Ordinal));
    }

    [Fact]
    public void WorldGenerator_DoesNotAddRepresentativeManagementFacts_WithoutOptIn()
    {
        var result = Generate(includeRepresentativeFacts: false);

        Assert.Empty(result.World.ManagementObservations);
        Assert.Empty(result.World.RelationshipHistoryObservations);
    }

    [Fact]
    public void WorldGenerator_DoesNotAddRepresentativeManagementFacts_WhenRequestedCountIsZero()
    {
        var result = Generate(includeRepresentativeFacts: true, representativeObservationCount: 0);

        Assert.Empty(result.World.ManagementObservations);
        Assert.Empty(result.World.RelationshipHistoryObservations);
    }

    [Fact]
    public void RelationshipHistory_PreservesInstalledOnFacts_WhenNoTruthfulOwnerExists()
    {
        var world = new SyntheticEnterpriseWorld();
        var company = new Company { Id = "COMP-001", Name = "Small Enterprise" };
        world.Companies.Add(company);
        world.Applications.Add(new ApplicationRecord
        {
            Id = "APP-001",
            CompanyId = company.Id,
            Name = "Operations Portal",
            OwnerDepartmentId = "DEP-UNSTAFFED",
        });
        world.ApplicationServices.Add(new ApplicationService
        {
            Id = "APPSVC-001",
            CompanyId = company.Id,
            ApplicationId = "APP-001",
            Name = "Operations Portal API",
        });
        world.Servers.Add(new ServerAsset
        {
            Id = "SRV-001",
            CompanyId = company.Id,
            Hostname = "operations-01",
        });
        world.SoftwarePackages.Add(new SoftwarePackage
        {
            Id = "SW-001",
            Name = "Application Runtime",
        });
        world.ServerSoftwareInstallations.Add(new ServerSoftwareInstallation
        {
            Id = "SSI-001",
            ServerId = "SRV-001",
            SoftwareId = "SW-001",
        });
        world.ApplicationServiceHostings.Add(new ApplicationServiceHosting
        {
            Id = "APPHST-001",
            CompanyId = company.Id,
            ApplicationServiceId = "APPSVC-001",
            HostType = "Server",
            HostId = "SRV-001",
            HostName = "operations-01",
        });

        RepresentativeManagementObservationGenerator.AddRepresentativeHistory(
            world,
            company,
            DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
            new TestIdFactory());

        var installedOn = world.RelationshipHistoryObservations
            .Where(observation => observation.RelationshipType == "InstalledOn")
            .ToArray();
        Assert.Equal(2, installedOn.Length);
        Assert.Contains(installedOn, observation => observation.LifecycleState == "Active");
        Assert.Contains(installedOn, observation => observation.LifecycleState == "Removed");
        Assert.All(installedOn, observation =>
        {
            Assert.Equal("APP-001", observation.FromEntityId);
            Assert.Equal("SRV-001", observation.ToEntityId);
            Assert.Equal(company.Id, observation.CompanyId);
        });
        Assert.DoesNotContain(world.RelationshipHistoryObservations, observation =>
            observation.RelationshipType == "Owns");
        Assert.Empty(world.People);
        Assert.Empty(world.Accounts);
    }

    [Fact]
    public void WorldGenerator_IsDeterministic_ForSameSeedScenarioAndGenerationTime()
    {
        var first = JsonSerializer.Serialize(Generate(includeRepresentativeFacts: true));
        var second = JsonSerializer.Serialize(Generate(includeRepresentativeFacts: true));

        Assert.Equal(first, second);
    }

    private static GenerationResult Generate(
        bool includeRepresentativeFacts,
        int representativeObservationCount = 15)
    {
        using var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var generator = services.GetRequiredService<IWorldGenerator>();
        return generator.Generate(
            new GenerationContext
            {
                Seed = 1130,
                GeneratedAt = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
                Scenario = CreateScenario(includeRepresentativeFacts, representativeObservationCount),
            },
            new CatalogSet());
    }

    private static ScenarioDefinition CreateScenario(
        bool includeRepresentativeFacts,
        int representativeObservationCount)
    {
        return new ScenarioDefinition
        {
            Name = "Representative management observations",
            IndustryProfile = "Manufacturing",
            Infrastructure = new InfrastructureProfile
            {
                IncludeServers = true,
                IncludeWorkstations = true,
                IncludeNetworkAssets = false,
                IncludeTelephony = false,
                IncludeRepresentativeManagementObservations = includeRepresentativeFacts,
                RepresentativeManagementObservationCount = representativeObservationCount,
            },
            Applications = new ApplicationProfile
            {
                IncludeApplications = true,
                BaseApplicationCount = 6,
                IncludeLineOfBusinessApplications = true,
                IncludeSaaSApplications = true,
            },
            Companies =
            [
                new ScenarioCompanyDefinition
                {
                    Name = "Representative Manufacturing",
                    Industry = "Manufacturing",
                    EmployeeCount = 120,
                    BusinessUnitCount = 2,
                    DepartmentCountPerBusinessUnit = 3,
                    TeamCountPerDepartment = 2,
                    OfficeCount = 2,
                    ServerCount = 12,
                    Countries = ["United States"],
                },
            ],
        };
    }

    private static string FindSourceRoot([CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (File.Exists(Path.Combine(
                    candidate,
                    "SyntheticEnterprise.Contracts",
                    "SyntheticEnterprise.Contracts.csproj")))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the DataGen production source root.");
    }

    private sealed class TestIdFactory : IIdFactory
    {
        private int _counter;

        public string Next(string entityType) => $"{entityType}-{++_counter:D6}";
    }
}
