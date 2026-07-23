using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

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
        Assert.Contains(world.RelationshipHistoryObservations, observation =>
            observation.RelationshipType == "InstalledOn"
            && observation.LifecycleState == "Removed");
        Assert.Contains(world.RelationshipHistoryObservations, observation =>
            observation.RelationshipType == "Owns"
            && observation.LifecycleState == "Removed");

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
    public void WorldGenerator_IsDeterministic_ForSameSeedScenarioAndGenerationTime()
    {
        var first = JsonSerializer.Serialize(Generate(includeRepresentativeFacts: true));
        var second = JsonSerializer.Serialize(Generate(includeRepresentativeFacts: true));

        Assert.Equal(first, second);
    }

    private static GenerationResult Generate(bool includeRepresentativeFacts)
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
                Scenario = CreateScenario(includeRepresentativeFacts),
            },
            new CatalogSet());
    }

    private static ScenarioDefinition CreateScenario(bool includeRepresentativeFacts)
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
}
