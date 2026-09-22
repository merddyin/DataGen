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
            var isCurrentHosting = world.ApplicationServiceHostings.Any(hosting =>
                hosting.CompanyId == application.CompanyId
                && hosting.HostType == "Server"
                && hosting.HostId == server.Id
                && servicesById[hosting.ApplicationServiceId].ApplicationId == application.Id);
            Assert.True(
                isCurrentHosting
                || (observation.SourceSystem == "ConfigurationManagement"
                    && observation.Detail?.Contains("inventory snapshot", StringComparison.OrdinalIgnoreCase) == true),
                "Installed-on history must be backed by either current hosting or a bounded inventory observation.");
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
            if (observation.LifecycleState == "Active")
            {
                Assert.Equal(application.OwnerDepartmentId, owner.DepartmentId);
            }
            else
            {
                Assert.Equal("Removed", observation.LifecycleState);
                Assert.False(string.IsNullOrWhiteSpace(owner.DepartmentId));
                Assert.True(string.IsNullOrWhiteSpace(application.OwnerDepartmentId));
            }
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
    public void WorldGenerator_MaterializesFormerApplicationOwnershipWithoutCurrentOwnershipEvidence()
    {
        var world = Generate(
            includeRepresentativeFacts: true,
            companyCount: 2,
            includeConfigurationManagement: true).World;
        var primaryCompany = world.Companies[0];
        var formerOwnership = Assert.Single(world.RelationshipHistoryObservations, observation =>
            observation.CompanyId == primaryCompany.Id
            && observation.RelationshipType == "Owns"
            && observation.LifecycleState == "Removed");
        var application = Assert.Single(world.Applications, candidate => candidate.Id == formerOwnership.ToEntityId);

        Assert.Equal(primaryCompany.Id, application.CompanyId);
        Assert.True(string.IsNullOrWhiteSpace(application.OwnerDepartmentId));
        Assert.DoesNotContain(world.RelationshipHistoryObservations, observation =>
            observation.CompanyId == primaryCompany.Id
            && observation.RelationshipType == "Owns"
            && observation.LifecycleState == "Active"
            && observation.ToEntityId == application.Id);

        var services = world.ApplicationServices
            .Where(service => service.CompanyId == primaryCompany.Id && service.ApplicationId == application.Id)
            .ToArray();
        Assert.NotEmpty(services);
        Assert.All(services, service => Assert.True(string.IsNullOrWhiteSpace(service.OwnerTeamId)));
        Assert.DoesNotContain(world.AccessControlEvidence, evidence =>
            evidence.CompanyId == primaryCompany.Id
            && evidence.TargetType == "Application"
            && evidence.TargetId == application.Id
            && evidence.RightName.EndsWith("Administration", StringComparison.OrdinalIgnoreCase));

        var applicationConfigurationItems = world.ConfigurationItems
            .Where(item => item.CompanyId == primaryCompany.Id
                && item.SourceEntityType == "Application"
                && item.SourceEntityId == application.Id)
            .ToArray();
        Assert.NotEmpty(applicationConfigurationItems);
        Assert.All(applicationConfigurationItems, item =>
        {
            Assert.True(string.IsNullOrWhiteSpace(item.BusinessOwnerPersonId));
            Assert.True(string.IsNullOrWhiteSpace(item.TechnicalOwnerPersonId));
            Assert.True(string.IsNullOrWhiteSpace(item.SupportTeamId));
            Assert.True(string.IsNullOrWhiteSpace(item.OwningDepartmentId));
        });

        var applicationConfigurationItemIds = applicationConfigurationItems
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var applicationSourceRecordIds = world.CmdbSourceLinks
            .Where(link => applicationConfigurationItemIds.Contains(link.ConfigurationItemId))
            .Select(link => link.SourceRecordId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(applicationSourceRecordIds);
        Assert.All(world.CmdbSourceRecords.Where(record => applicationSourceRecordIds.Contains(record.Id)), record =>
        {
            Assert.True(string.IsNullOrWhiteSpace(record.ObservedBusinessOwner));
            Assert.True(string.IsNullOrWhiteSpace(record.ObservedTechnicalOwner));
            Assert.True(string.IsNullOrWhiteSpace(record.ObservedSupportGroup));
        });

        Assert.All(world.ObservedEntitySnapshots.Where(snapshot =>
                snapshot.CompanyId == primaryCompany.Id
                && ((snapshot.EntityType == "Application" && snapshot.EntityId == application.Id)
                    || (snapshot.EntityType == "ApplicationService" && services.Any(service => service.Id == snapshot.EntityId)))),
            snapshot => Assert.True(string.IsNullOrWhiteSpace(snapshot.OwnerReference)));
    }

    [Fact]
    public void WorldGenerator_CoversEndpointPopulationWithCohortDominantProvidersAndBoundedOutliers()
    {
        var world = Generate(
            includeRepresentativeFacts: true,
            managementObservationPopulationCoveragePercentage: 100).World;
        var current = world.ManagementObservations
            .Where(observation => observation.IsCurrent)
            .ToArray();
        var endpoints = world.Devices
            .Select(device => new { EndpointType = "Device", EndpointId = device.Id, device.OperatingSystem })
            .Concat(world.Servers.Select(server => new { EndpointType = "Server", EndpointId = server.Id, server.OperatingSystem }))
            .ToArray();

        Assert.True(current.Length >= endpoints.Length);
        Assert.All(endpoints, endpoint =>
            Assert.Contains(current, observation =>
                observation.EndpointType == endpoint.EndpointType
                && observation.EndpointId == endpoint.EndpointId));

        var windowsDevices = endpoints
            .Where(endpoint => endpoint.EndpointType == "Device"
                && endpoint.OperatingSystem.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var macDevices = endpoints
            .Where(endpoint => endpoint.EndpointType == "Device"
                && endpoint.OperatingSystem.StartsWith("macOS", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var windowsServers = endpoints
            .Where(endpoint => endpoint.EndpointType == "Server"
                && endpoint.OperatingSystem.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(CurrentProviderCount(current, windowsDevices.Select(endpoint => (endpoint.EndpointType, endpoint.EndpointId)), "MicrosoftIntune") * 100 >= windowsDevices.Length * 80);
        Assert.True(CurrentProviderCount(current, macDevices.Select(endpoint => (endpoint.EndpointType, endpoint.EndpointId)), "Jamf") * 100 >= macDevices.Length * 80);
        Assert.True(CurrentProviderCount(current, windowsServers.Select(endpoint => (endpoint.EndpointType, endpoint.EndpointId)), "ConfigurationManager") * 100 >= windowsServers.Length * 80);
        Assert.Contains(current, observation =>
            observation.RegistrationState == "Registered"
            && observation.LastCheckInAtUtc <= observation.ObservedAtUtc.AddDays(-21));
        Assert.Contains(current, observation =>
            observation.ManagementProvider is "ConfigurationManager" or "Rmm" or "BigFix" or "Puppet" or "Ansible"
            && observation.DeploymentCapability == "Supported");
    }

    [Fact]
    public void WorldGenerator_KeepsManagementEvidenceCoherentWithInstalledAgentInventory()
    {
        var result = Generate(
            includeRepresentativeFacts: true,
            catalogs: CreateManagementAgentCatalog());
        var replay = Generate(
            includeRepresentativeFacts: true,
            catalogs: CreateManagementAgentCatalog());
        var world = result.World;

        Assert.Equal(
            JsonSerializer.Serialize(ProjectManagementAgentInventory(result.World)),
            JsonSerializer.Serialize(ProjectManagementAgentInventory(replay.World)));
        var softwareById = world.SoftwarePackages.ToDictionary(package => package.Id, StringComparer.Ordinal);
        var current = world.ManagementObservations
            .Where(observation => observation.IsCurrent
                && string.Equals(observation.LifecycleState, "Current", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(observation.SupersededByObservationId))
            .ToArray();
        var unmanaged = current
            .Where(observation => string.Equals(observation.ManagementProvider, "None", StringComparison.OrdinalIgnoreCase)
                || string.Equals(observation.RegistrationState, "NotRegistered", StringComparison.OrdinalIgnoreCase)
                || string.Equals(observation.DeploymentCapability, "Unsupported", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(unmanaged);
        Assert.All(current.Where(observation => observation.RegistrationState == "Registered"), observation =>
        {
            Assert.False(string.IsNullOrWhiteSpace(observation.AgentSoftwareId));
            var agentSoftwareId = observation.AgentSoftwareId!;
            Assert.True(softwareById.TryGetValue(agentSoftwareId, out var package));
            Assert.True(ManagementAgentCatalog.IsKnownDeploymentAgent(package!));
            Assert.True(IsInstalled(world, observation.EndpointType, observation.EndpointId, agentSoftwareId));
        });

        Assert.All(unmanaged, observation =>
        {
            Assert.Null(observation.AgentSoftwareId);
            Assert.DoesNotContain(
                InstalledSoftware(world, observation.EndpointType, observation.EndpointId),
                softwareId => ManagementAgentCatalog.IsKnownDeploymentAgent(softwareById[softwareId]));
        });

        Assert.All(
            ["Tanium Client", "SCCM Client", "ServiceNow Agent"],
            agentName =>
            {
                var package = Assert.Single(world.SoftwarePackages, candidate => candidate.Name == agentName);
                Assert.DoesNotContain(world.DeviceSoftwareInstallations, installation => installation.SoftwareId == package.Id);
                Assert.DoesNotContain(world.ServerSoftwareInstallations, installation => installation.SoftwareId == package.Id);
            });

        var securityBaseline = Assert.Single(world.SoftwarePackages, package => package.Name == "CrowdStrike Falcon");
        Assert.All(world.Devices, device =>
            Assert.True(IsInstalled(world, "Device", device.Id, securityBaseline.Id)));
    }

    [Fact]
    public void WorldGenerator_EmitsDirectoryBackedPreferredPathGapsWithViableAlternates()
    {
        var world = Generate(
            includeRepresentativeFacts: true,
            managementObservationPopulationCoveragePercentage: 100).World;
        var devicesById = world.Devices.ToDictionary(device => device.Id, StringComparer.Ordinal);
        var actionable = world.ManagementObservations
            .Where(observation => observation.IsCurrent
                && string.Equals(observation.LifecycleState, "Current", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(observation.SupersededByObservationId)
                && observation.LastCheckInAtUtc >= observation.ObservedAtUtc.AddDays(-20)
                && observation.DeploymentCapability is "Supported" or "Available" or "Viable")
            .ToArray();
        var commonProviderByCohort = actionable
            .GroupBy(observation => $"{observation.CompanyId}|{observation.OperatingSystemFamily}|{observation.Cohort}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.GroupBy(observation => observation.ManagementProvider, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(provider => provider.Count())
                    .ThenBy(provider => provider.Key, StringComparer.Ordinal)
                    .First().Key,
                StringComparer.OrdinalIgnoreCase);
        var pairedAlternates = actionable
            .Where(alternate => alternate.EndpointType == "Device"
                && devicesById.TryGetValue(alternate.EndpointId, out var device)
                && device.DomainJoined
                && !string.IsNullOrWhiteSpace(device.DirectoryAccountId)
                && string.Equals(device.DirectoryAccountId, alternate.DeviceAccountId, StringComparison.Ordinal)
                && commonProviderByCohort.TryGetValue(
                    $"{alternate.CompanyId}|{alternate.OperatingSystemFamily}|{alternate.Cohort}",
                    out var expectedProvider)
                && !string.Equals(expectedProvider, alternate.ManagementProvider, StringComparison.OrdinalIgnoreCase)
                && world.ManagementObservations.Any(expected =>
                    expected.CompanyId == alternate.CompanyId
                    && expected.EndpointType == alternate.EndpointType
                    && expected.EndpointId == alternate.EndpointId
                    && expected.OperatingSystemFamily == alternate.OperatingSystemFamily
                    && expected.Cohort == alternate.Cohort
                    && string.Equals(expected.ManagementProvider, expectedProvider, StringComparison.OrdinalIgnoreCase)
                    && expected.IsCurrent
                    && string.Equals(expected.LifecycleState, "Current", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(expected.SupersededByObservationId)
                    && expected.LastCheckInAtUtc < expected.ObservedAtUtc.AddDays(-20)
                    && string.Equals(expected.DeviceAccountId, alternate.DeviceAccountId, StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(pairedAlternates);
        Assert.All(pairedAlternates, alternate =>
        {
            Assert.Contains(alternate.ManagementProvider, new[] { "ConfigurationManager", "Rmm", "BigFix", "Puppet", "Ansible" });
            Assert.Equal("Supported", alternate.DeploymentCapability);
        });
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
    public void Apply_CorrelatesServerManagementObservationsWithSelectedServerHostingFacts()
    {
        var world = GenerateManagementObservations(
            requestedCount: 6,
            hostedPercentage: 50,
            CreateServer("SRV-001", "OnPremises"),
            CreateServer("SRV-002", "Cloud", "Azure"),
            CreateServer("SRV-003", "Cloud", "AWS"));
        var serversById = world.Servers.ToDictionary(server => server.Id, StringComparer.Ordinal);
        var observations = CurrentServerObservations(world);

        Assert.Equal(3, observations.Length);
        Assert.All(observations.Where(observation => observation.HostingEnvironmentKind == "HostedCompute"), observation =>
        {
            var server = serversById[observation.EndpointId];
            Assert.Equal("Cloud", server.HostingLocationType);
            Assert.Equal(server.CloudProvider, observation.HostingProvider);
        });
        Assert.All(observations.Where(observation => observation.HostingEnvironmentKind == "NonHosted"), observation =>
        {
            var server = serversById[observation.EndpointId];
            Assert.Equal("OnPremises", server.HostingLocationType);
            Assert.Null(server.CloudProvider);
            Assert.Null(observation.HostingProvider);
            Assert.Equal("Unavailable", observation.OutOfBandGuestDeploymentCapability);
        });

        var azure = Assert.Single(observations, observation => observation.EndpointId == "SRV-002");
        Assert.Equal("HostedCompute", azure.HostingEnvironmentKind);
        Assert.Equal("Azure", azure.HostingProvider);
        Assert.Equal("Supported", azure.OutOfBandGuestDeploymentCapability);
    }

    [Fact]
    public void Apply_TreatsExplicitOnPremisesAsNonHostedWhenCloudProviderIsPresent()
    {
        var world = GenerateManagementObservations(
            requestedCount: 6,
            hostedPercentage: 100,
            CreateServer("SRV-001", "OnPremises", "Azure"));

        var observation = Assert.Single(CurrentServerObservations(world));

        Assert.Equal("NonHosted", observation.HostingEnvironmentKind);
        Assert.Null(observation.HostingProvider);
        Assert.Equal("Unavailable", observation.OutOfBandGuestDeploymentCapability);
    }

    [Fact]
    public void Apply_UsesCloudProviderFallbackForMissingOrUnknownHostingLocationType()
    {
        var world = GenerateManagementObservations(
            requestedCount: 6,
            hostedPercentage: 100,
            CreateServer("SRV-001", "Legacy", "Azure"),
            CreateServer("SRV-002", null, "AWS"));

        var observations = CurrentServerObservations(world);

        Assert.Equal(2, observations.Length);
        Assert.All(observations, observation =>
            Assert.Equal("HostedCompute", observation.HostingEnvironmentKind));
        Assert.Equal("Azure", Assert.Single(observations, observation => observation.EndpointId == "SRV-001").HostingProvider);
        Assert.Equal("AWS", Assert.Single(observations, observation => observation.EndpointId == "SRV-002").HostingProvider);
    }

    [Fact]
    public void Apply_FillsAZeroPercentHostedTargetWithHostedServersWhenNoOnPremisesServersExist()
    {
        var world = GenerateManagementObservations(
            requestedCount: 6,
            hostedPercentage: 0,
            CreateServer("SRV-001", "Cloud", "Azure"),
            CreateServer("SRV-002", "Cloud", "AWS"));

        var observations = CurrentServerObservations(world);

        Assert.Equal(2, observations.Length);
        Assert.All(observations, observation => Assert.Equal("HostedCompute", observation.HostingEnvironmentKind));
        Assert.All(observations, observation => Assert.False(string.IsNullOrWhiteSpace(observation.HostingProvider)));
    }

    [Fact]
    public void Apply_FillsAHundredPercentHostedTargetWithOnPremisesServersWhenNoHostedServersExist()
    {
        var world = GenerateManagementObservations(
            requestedCount: 6,
            hostedPercentage: 100,
            CreateServer("SRV-001", "OnPremises"),
            CreateServer("SRV-002", "OnPremises"));

        var observations = CurrentServerObservations(world);

        Assert.Equal(2, observations.Length);
        Assert.All(observations, observation => Assert.Equal("NonHosted", observation.HostingEnvironmentKind));
        Assert.All(observations, observation => Assert.Null(observation.HostingProvider));
        Assert.All(observations, observation => Assert.Equal("Unavailable", observation.OutOfBandGuestDeploymentCapability));
    }

    [Fact]
    public void Apply_FillsAHundredPercentHostedTargetWithOnPremisesServersWhenHostedCapacityIsInsufficient()
    {
        var world = GenerateManagementObservations(
            requestedCount: 12,
            hostedPercentage: 100,
            CreateServer("SRV-001", "OnPremises"),
            CreateServer("SRV-002", "OnPremises"),
            CreateServer("SRV-003", "Cloud", "Azure"),
            CreateServer("SRV-004", "Cloud", "AWS"));

        var observations = CurrentServerObservations(world);

        Assert.Equal(4, observations.Length);
        Assert.Equal(2, observations.Count(observation => observation.HostingEnvironmentKind == "HostedCompute"));
        Assert.Equal(2, observations.Count(observation => observation.HostingEnvironmentKind == "NonHosted"));
    }

    [Fact]
    public void Apply_FillsServerObservationBudgetFromAvailableHostingCategories()
    {
        var world = GenerateManagementObservations(
            requestedCount: 12,
            hostedPercentage: 50,
            CreateServer("SRV-001", "OnPremises"),
            CreateServer("SRV-002", "OnPremises"),
            CreateServer("SRV-003", "Cloud", "Azure"));

        var observations = CurrentServerObservations(world);

        Assert.Equal(3, observations.Length);
        Assert.Equal(1, observations.Count(observation => observation.HostingEnvironmentKind == "HostedCompute"));
        Assert.Equal(2, observations.Count(observation => observation.HostingEnvironmentKind == "NonHosted"));
    }

    [Fact]
    public void Apply_DoesNotEmitServerManagementObservationsWhenNoServersExist()
    {
        var world = GenerateManagementObservations(
            requestedCount: 6,
            hostedPercentage: 50);

        Assert.Empty(CurrentServerObservations(world));
    }

    [Fact]
    public void Apply_IsDeterministicForSameSeedTimeSettingsAndHostingPopulation()
    {
        var first = GenerateManagementObservations(
            requestedCount: 6,
            hostedPercentage: 50,
            CreateServer("SRV-001", "OnPremises"),
            CreateServer("SRV-002", "Cloud", "Azure"),
            CreateServer("SRV-003", "Cloud", "AWS"));
        var second = GenerateManagementObservations(
            requestedCount: 6,
            hostedPercentage: 50,
            CreateServer("SRV-001", "OnPremises"),
            CreateServer("SRV-002", "Cloud", "Azure"),
            CreateServer("SRV-003", "Cloud", "AWS"));

        Assert.Equal(
            JsonSerializer.Serialize(first.ManagementObservations),
            JsonSerializer.Serialize(second.ManagementObservations));
    }

    [Fact]
    public void EndpointManagementObservation_DefaultsExistingConstructedRowsToCurrent()
    {
        var observation = new EndpointManagementObservation();

        Assert.Equal("Current", observation.LifecycleState);
        Assert.True(observation.IsCurrent);
        Assert.Null(observation.SupersededByObservationId);
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
    public void Apply_DefaultPopulationCoveragePreservesRepresentativeObservationCount()
    {
        var world = CreatePopulationWorld(companyCount: 1, endpointsPerCompany: 20);
        var company = Assert.Single(world.Companies);

        RepresentativeManagementObservationGenerator.Apply(
            world,
            company,
            CreateGenerationContext(),
            new TestIdFactory(),
            new InfrastructureProfile
            {
                RepresentativeManagementObservationCount = 3,
                RepresentativeManagementHistoryObservationCount = 0,
            });

        Assert.Equal(3, world.ManagementObservations.Count(observation => observation.IsCurrent));
    }

    [Fact]
    public void Apply_DefaultPopulationCoveragePreservesLegacySoftwareDefinitionsAndInstallationIdentityOrder()
    {
        var world = CreatePopulationWorld(companyCount: 1, endpointsPerCompany: 3);
        var company = Assert.Single(world.Companies);

        RepresentativeManagementObservationGenerator.Apply(
            world,
            company,
            CreateGenerationContext(),
            new TestIdFactory(),
            new InfrastructureProfile
            {
                RepresentativeManagementObservationCount = 3,
                RepresentativeManagementHistoryObservationCount = 0,
            });

        Assert.Equal(
            [
                "SW-000001|Microsoft Intune Management Extension",
                "SW-000002|Configuration Manager Client",
                "SW-000003|BigFix Client",
                "SW-000004|Remote Monitoring Agent",
                "SW-000005|Ansible Automation Platform",
            ],
            world.SoftwarePackages.Select(package => $"{package.Id}|{package.Name}"));
        Assert.Equal(
            [
                "DSI-000006|DEV-1-001|SW-000001",
                "DSI-000008|DEV-1-002|SW-000002",
                "DSI-000010|DEV-1-003|SW-000003",
            ],
            world.DeviceSoftwareInstallations.Select(installation =>
                $"{installation.Id}|{installation.DeviceId}|{installation.SoftwareId}"));
        Assert.Equal(3, world.ManagementObservations.Count(observation => observation.IsCurrent));
    }

    [Fact]
    public void Apply_PopulationCoverageSupportsEveryPercentageDeterministicallyPerCompany()
    {
        for (var percentage = 1; percentage <= 100; percentage++)
        {
            var first = GeneratePopulationCoverage(percentage, companyCount: 2, endpointsPerCompany: 20);
            var replay = GeneratePopulationCoverage(percentage, companyCount: 2, endpointsPerCompany: 20);
            var expectedPerCompany = (int)Math.Ceiling(20 * percentage / 100m);

            Assert.Equal(
                first.ManagementObservations.Select(ProjectObservation),
                replay.ManagementObservations.Select(ProjectObservation));
            Assert.All(first.Companies, company =>
            {
                var observations = first.ManagementObservations
                    .Where(observation => observation.CompanyId == company.Id)
                    .ToArray();
                Assert.Equal(expectedPerCompany, observations.Length);
                Assert.All(observations, observation =>
                {
                    var device = Assert.Single(first.Devices, device => device.Id == observation.EndpointId);
                    Assert.Equal(company.Id, device.CompanyId);
                });
            });
        }
    }

    [Fact]
    public void Apply_PopulationCoverageDoesNotDuplicateExistingEndpointSoftwarePairs()
    {
        var world = CreatePopulationWorld(companyCount: 1, endpointsPerCompany: 1);
        var company = Assert.Single(world.Companies);
        world.SoftwarePackages.Add(new SoftwarePackage
        {
            Id = "SW-INTUNE",
            Name = "Microsoft Intune Management Extension",
        });
        world.DeviceSoftwareInstallations.Add(new DeviceSoftwareInstallation
        {
            Id = "DSI-EXISTING",
            DeviceId = "DEV-1-001",
            SoftwareId = "SW-INTUNE",
        });
        world.Servers.Add(new ServerAsset
        {
            Id = "SRV-001",
            CompanyId = company.Id,
            Hostname = "server-001",
            OperatingSystem = "Windows Server 2022",
        });
        world.SoftwarePackages.Add(new SoftwarePackage
        {
            Id = "SW-CONFIGURATION-MANAGER",
            Name = "Configuration Manager Client",
        });
        world.ServerSoftwareInstallations.Add(new ServerSoftwareInstallation
        {
            Id = "SSI-EXISTING",
            ServerId = "SRV-001",
            SoftwareId = "SW-CONFIGURATION-MANAGER",
        });

        RepresentativeManagementObservationGenerator.Apply(
            world,
            company,
            CreateGenerationContext(),
            new TestIdFactory(),
            new InfrastructureProfile
            {
                RepresentativeManagementObservationCount = 1,
                RepresentativeManagementHistoryObservationCount = 0,
                ManagementObservationPopulationCoveragePercentage = 100,
            });

        Assert.Single(world.DeviceSoftwareInstallations, installation =>
            installation.DeviceId == "DEV-1-001"
            && installation.SoftwareId == "SW-INTUNE");
        Assert.Single(world.ServerSoftwareInstallations, installation =>
            installation.ServerId == "SRV-001"
            && installation.SoftwareId == "SW-CONFIGURATION-MANAGER");
    }

    [Fact]
    public void Apply_PopulationCoverageDoesNotInjectRareOutliersIntoSmallCohorts()
    {
        var world = new SyntheticEnterpriseWorld();
        var company = new Company { Id = "CO-001", Name = "Small hosted cohort" };
        world.Companies.Add(company);
        for (var index = 1; index <= 49; index++)
        {
            world.Servers.Add(new ServerAsset
            {
                Id = $"SRV-{index:000}",
                CompanyId = company.Id,
                Hostname = $"hosted-{index:000}",
                OperatingSystem = "Windows Server 2022",
                HostingLocationType = "Cloud",
                CloudProvider = "RepresentativeCloud",
            });
        }

        RepresentativeManagementObservationGenerator.Apply(
            world,
            company,
            CreateGenerationContext(),
            new TestIdFactory(),
            new InfrastructureProfile
            {
                RepresentativeManagementObservationCount = 1,
                RepresentativeManagementHistoryObservationCount = 0,
                ManagementObservationPopulationCoveragePercentage = 100,
            });

        Assert.Equal(49, world.ManagementObservations.Count);
        Assert.All(world.ManagementObservations, observation =>
        {
            Assert.Equal("ConfigurationManager", observation.ManagementProvider);
            Assert.Equal("Registered", observation.RegistrationState);
            Assert.Equal("Unknown", observation.OutOfBandGuestDeploymentCapability);
            Assert.Equal(TimeSpan.FromDays(2), observation.ObservedAtUtc - observation.LastCheckInAtUtc);
        });
    }

    [Fact]
    public void Apply_PopulationCoverageIntroducesHostedFallbackAtFiftySelectedHostedEndpoints()
    {
        var world = new SyntheticEnterpriseWorld();
        var company = new Company { Id = "CO-001", Name = "Hosted fallback threshold" };
        world.Companies.Add(company);
        for (var index = 1; index <= 50; index++)
        {
            world.Servers.Add(new ServerAsset
            {
                Id = $"SRV-{index:000}",
                CompanyId = company.Id,
                Hostname = $"hosted-{index:000}",
                OperatingSystem = "Windows Server 2022",
                HostingLocationType = "Cloud",
                CloudProvider = "RepresentativeCloud",
            });
        }

        RepresentativeManagementObservationGenerator.Apply(
            world,
            company,
            CreateGenerationContext(),
            new TestIdFactory(),
            new InfrastructureProfile
            {
                RepresentativeManagementObservationCount = 1,
                RepresentativeManagementHistoryObservationCount = 0,
                ManagementObservationPopulationCoveragePercentage = 100,
            });

        var fallback = Assert.Single(world.ManagementObservations, observation =>
            observation.OutOfBandGuestDeploymentCapability == "Supported");
        Assert.Equal("None", fallback.ManagementProvider);
        Assert.Equal("NotRegistered", fallback.RegistrationState);
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
        Assert.Single(installedOn);
        Assert.Contains(installedOn, observation => observation.LifecycleState == "Active");
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
    public void WorldGenerator_ProducesSeparateCurrentAndRetiredRelationshipEvidence()
    {
        var world = Generate(includeRepresentativeFacts: true).World;

        Assert.Equal(
            world.RelationshipHistoryObservations.Count,
            world.RelationshipHistoryObservations.Select(observation => observation.Id).Distinct(StringComparer.Ordinal).Count());
        var relationshipGroups = world.RelationshipHistoryObservations
            .GroupBy(observation => new
            {
                observation.CompanyId,
                observation.RelationshipType,
                observation.FromEntityType,
                observation.FromEntityId,
                observation.ToEntityType,
                observation.ToEntityId,
                observation.SourceSystem,
            })
            .ToArray();
        Assert.NotEmpty(relationshipGroups);
        Assert.Contains(world.RelationshipHistoryObservations, observation =>
            observation.RelationshipType == "InstalledOn" && observation.LifecycleState == "Active");
        Assert.Contains(world.RelationshipHistoryObservations, observation =>
            observation.RelationshipType == "InstalledOn" && observation.LifecycleState == "Removed");
        Assert.Contains(world.RelationshipHistoryObservations, observation =>
            observation.RelationshipType == "Owns" && observation.LifecycleState == "Active");
        Assert.Contains(world.RelationshipHistoryObservations, observation =>
            observation.RelationshipType == "Owns" && observation.LifecycleState == "Removed");
        Assert.All(relationshipGroups, group =>
        {
            var history = Assert.Single(group);
            if (history.LifecycleState == "Removed")
            {
                Assert.NotNull(history.RemovedAtUtc);
                Assert.True(history.ObservedAtUtc < history.RemovedAtUtc);
            }
            else
            {
                Assert.Equal("Active", history.LifecycleState);
                Assert.Null(history.RemovedAtUtc);
            }
        });

        Assert.Equal(
            world.ManagementObservations.Count,
            world.ManagementObservations.Select(observation => observation.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(15, world.ManagementObservations.Count(observation => observation.IsCurrent));
        var managementHistoryGroups = GetManagementHistoryGroups(world);
        var managementHistory = Assert.Single(managementHistoryGroups);
        AssertManagementHistory(managementHistory, expectedHistoryCount: 1);
    }

    [Fact]
    public void WorldGenerator_ReplaysTemporalCorrectionsForSameSeedAndVariesThemForDifferentSeeds()
    {
        var firstWorld = Generate(includeRepresentativeFacts: true, seed: 1130).World;
        var replayWorld = Generate(includeRepresentativeFacts: true, seed: 1130).World;
        var first = ProjectTemporalCorrections(firstWorld);
        var replay = ProjectTemporalCorrections(replayWorld);

        Assert.Equal(first.Relationships, replay.Relationships);
        Assert.Equal(first.Management, replay.Management);
        Assert.All(GetManagementHistoryGroups(firstWorld), history => AssertManagementHistory(history, expectedHistoryCount: 1));

        var variations = new[] { 1130, 2210, 3901 }
            .Select(seed => Generate(includeRepresentativeFacts: true, seed: seed).World)
            .Select(world =>
            {
                var history = Assert.Single(GetManagementHistoryGroups(world));
                AssertManagementHistory(history, expectedHistoryCount: 1);
                var historical = Assert.Single(history, observation => !observation.IsCurrent);
                var current = Assert.Single(history, observation => observation.IsCurrent);
                return $"{(current.ObservedAtUtc - historical.ObservedAtUtc).Ticks}|{(historical.ObservedAtUtc - historical.LastCheckInAtUtc!.Value).Ticks}";
            })
            .ToArray();
        Assert.True(variations.Distinct(StringComparer.Ordinal).Count() > 1);
        Assert.All(first.Management, observation =>
        {
            Assert.DoesNotContain("cartograph", observation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("review queue", observation, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void WorldGenerator_CountOneIncludesOneCurrentAndOneBudgetedHistoricalObservation()
    {
        var world = Generate(
            includeRepresentativeFacts: true,
            representativeObservationCount: 1,
            representativeHistoryObservationCount: 1,
            managementObservationPopulationCoveragePercentage: 0).World;

        Assert.Equal(2, world.ManagementObservations.Count);
        Assert.Single(world.ManagementObservations, observation => observation.IsCurrent);
        Assert.Single(world.ManagementObservations, observation => !observation.IsCurrent);
        var current = Assert.Single(world.ManagementObservations, observation => observation.IsCurrent);
        Assert.Equal(TimeSpan.FromDays(1), current.ObservedAtUtc - current.LastCheckInAtUtc!.Value);
        var history = Assert.Single(GetManagementHistoryGroups(world));
        AssertManagementHistory(history, expectedHistoryCount: 1);
    }

    [Fact]
    public void Apply_CountOneAtDateTimeOffsetMinimum_DoesNotThrowAndPreservesValidHistoryBoundary()
    {
        var world = new SyntheticEnterpriseWorld();
        var company = new Company { Id = "CO-001", Name = "Boundary-safe management" };
        world.Companies.Add(company);
        world.Servers.Add(new ServerAsset
        {
            Id = "SRV-001",
            CompanyId = company.Id,
            Hostname = "boundary-safe-01",
            OperatingSystem = "Windows Server",
        });

        RepresentativeManagementObservationGenerator.Apply(
            world,
            company,
            new GenerationContext
            {
                Scenario = new ScenarioDefinition { Name = "Boundary-safe management" },
                Seed = 1130,
                GeneratedAt = DateTimeOffset.MinValue,
            },
            new TestIdFactory(),
            new InfrastructureProfile
            {
                IncludeServers = true,
                IncludeWorkstations = false,
                IncludeNetworkAssets = false,
                IncludeTelephony = false,
                RepresentativeManagementObservationCount = 1,
                RepresentativeManagementHistoryObservationCount = 1,
            });

        Assert.Equal(1, world.ManagementObservations.Count(observation => observation.IsCurrent));
        var history = world.ManagementObservations
            .OrderBy(observation => observation.ObservedAtUtc)
            .ToArray();
        Assert.InRange(history.Length, 1, 2);

        if (history.Length == 2)
        {
            var historical = Assert.Single(history, observation => !observation.IsCurrent);
            var current = Assert.Single(history, observation => observation.IsCurrent);
            Assert.True(historical.LastCheckInAtUtc < historical.ObservedAtUtc);
            Assert.True(historical.ObservedAtUtc < current.ObservedAtUtc);
        }
        else
        {
            Assert.Single(history, observation => observation.IsCurrent);
        }
    }

    [Fact]
    public void WorldGenerator_DefaultHistoryBudgetDemonstratesCountOneCorrectionStory()
    {
        var scenario = CreateScenario(
            includeRepresentativeFacts: true,
            representativeObservationCount: 1,
            representativeHistoryObservationCount: 1,
            companyCount: 1) with
        {
            Infrastructure = new InfrastructureProfile
            {
                IncludeServers = true,
                IncludeWorkstations = true,
                IncludeNetworkAssets = false,
                IncludeTelephony = false,
                IncludeRepresentativeManagementObservations = true,
                RepresentativeManagementObservationCount = 1,
                ManagementObservationPopulationCoveragePercentage = 0,
            },
        };
        using var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var generator = services.GetRequiredService<IWorldGenerator>();

        var world = generator.Generate(
            new GenerationContext
            {
                Scenario = scenario,
                Seed = 1130,
                GeneratedAt = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
            },
            new CatalogSet()).World;

        Assert.Equal(1, scenario.Infrastructure.RepresentativeManagementHistoryObservationCount);
        Assert.Equal(2, world.ManagementObservations.Count);
        AssertManagementHistory(Assert.Single(GetManagementHistoryGroups(world)), expectedHistoryCount: 1);
    }

    [Fact]
    public void WorldGenerator_MultiCompanyHistoryStaysWithinSeparatePerCompanyBudgets()
    {
        const int companyCount = 2;
        const int currentBudget = 3;
        const int historyBudget = 2;
        var world = Generate(
            includeRepresentativeFacts: true,
            representativeObservationCount: currentBudget,
            representativeHistoryObservationCount: historyBudget,
            companyCount: companyCount,
            managementObservationPopulationCoveragePercentage: 0).World;

        Assert.Equal(companyCount * (currentBudget + historyBudget), world.ManagementObservations.Count);
        Assert.Equal(
            world.ManagementObservations.Count,
            world.ManagementObservations.Select(observation => observation.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(world.Companies, company =>
        {
            var companyObservations = world.ManagementObservations
                .Where(observation => observation.CompanyId == company.Id)
                .ToArray();
            Assert.Equal(currentBudget, companyObservations.Count(observation => observation.IsCurrent));
            Assert.Equal(historyBudget, companyObservations.Count(observation => !observation.IsCurrent));
            Assert.True(companyObservations.Length <= currentBudget + historyBudget);
        });
        Assert.Equal(companyCount * historyBudget, GetManagementHistoryGroups(world).Length);
        Assert.All(GetManagementHistoryGroups(world), history => AssertManagementHistory(history, expectedHistoryCount: 1));
    }

    [Fact]
    public void AddStaleManagementHistory_ClampsAtDateTimeOffsetLowerBoundWithoutBreakingOrdering()
    {
        var world = new SyntheticEnterpriseWorld();
        world.ManagementObservations.Add(CreateCurrentObservation(
            "MGO-CURRENT",
            DateTimeOffset.MinValue.AddTicks(2)));

        RepresentativeManagementObservationGenerator.AddStaleManagementHistory(
            world,
            "CO-001",
            new GenerationContext
            {
                Scenario = new ScenarioDefinition { Name = "Boundary-safe management history" },
                Seed = 1130,
            },
            new TestIdFactory(),
            historyBudget: 1);

        var history = Assert.Single(GetManagementHistoryGroups(world));
        var historical = Assert.Single(history, observation => !observation.IsCurrent);
        var current = Assert.Single(history, observation => observation.IsCurrent);
        Assert.Equal(DateTimeOffset.MinValue, historical.LastCheckInAtUtc);
        Assert.True(historical.LastCheckInAtUtc < historical.ObservedAtUtc);
        Assert.True(historical.ObservedAtUtc < current.ObservedAtUtc);

        var minimumWorld = new SyntheticEnterpriseWorld();
        minimumWorld.ManagementObservations.Add(CreateCurrentObservation("MGO-MINIMUM", DateTimeOffset.MinValue));
        RepresentativeManagementObservationGenerator.AddStaleManagementHistory(
            minimumWorld,
            "CO-001",
            new GenerationContext
            {
                Scenario = new ScenarioDefinition { Name = "Minimum management history" },
                Seed = 1130,
            },
            new TestIdFactory(),
            historyBudget: 1);
        Assert.Single(minimumWorld.ManagementObservations);
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
        int representativeObservationCount = 15,
        int representativeHistoryObservationCount = 1,
        int seed = 1130,
        int companyCount = 1,
        int managementObservationPopulationCoveragePercentage = 0,
        bool includeConfigurationManagement = false,
        CatalogSet? catalogs = null)
    {
        using var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();
        var generator = services.GetRequiredService<IWorldGenerator>();
        return generator.Generate(
            new GenerationContext
            {
                Seed = seed,
                GeneratedAt = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
                Scenario = CreateScenario(
                    includeRepresentativeFacts,
                    representativeObservationCount,
                    representativeHistoryObservationCount,
                    companyCount,
                    managementObservationPopulationCoveragePercentage,
                    includeConfigurationManagement),
            },
            catalogs ?? new CatalogSet());
    }

    private static CatalogSet CreateManagementAgentCatalog()
        => new()
        {
            CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["software_catalog"] =
                [
                    SoftwareCatalogRow("CrowdStrike Falcon", "Security", "CrowdStrike", "7.2"),
                    SoftwareCatalogRow("Tanium Client", "Security", "Tanium", "7.6"),
                    SoftwareCatalogRow("SCCM Client", "Utility", "Microsoft", "5.00"),
                    SoftwareCatalogRow("ServiceNow Agent", "Utility", "ServiceNow", "1.0"),
                ],
            },
        };

    private static Dictionary<string, string?> SoftwareCatalogRow(
        string name,
        string category,
        string vendor,
        string version)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = name,
            ["Category"] = category,
            ["Vendor"] = vendor,
            ["Version"] = version,
        };

    private static IEnumerable<string> InstalledSoftware(
        SyntheticEnterpriseWorld world,
        string endpointType,
        string endpointId)
        => endpointType == "Device"
            ? world.DeviceSoftwareInstallations
                .Where(installation => installation.DeviceId == endpointId)
                .Select(installation => installation.SoftwareId)
            : world.ServerSoftwareInstallations
                .Where(installation => installation.ServerId == endpointId)
                .Select(installation => installation.SoftwareId);

    private static bool IsInstalled(
        SyntheticEnterpriseWorld world,
        string endpointType,
        string endpointId,
        string softwareId)
        => InstalledSoftware(world, endpointType, endpointId)
            .Contains(softwareId, StringComparer.Ordinal);

    private static object ProjectManagementAgentInventory(SyntheticEnterpriseWorld world)
        => new
        {
            Observations = world.ManagementObservations
                .OrderBy(observation => observation.Id, StringComparer.Ordinal)
                .Select(observation => new
                {
                    observation.Id,
                    observation.EndpointType,
                    observation.EndpointId,
                    observation.ManagementProvider,
                    observation.AgentSoftwareId,
                    observation.RegistrationState,
                    observation.DeploymentCapability,
                }),
            DeviceInstallations = world.DeviceSoftwareInstallations
                .OrderBy(installation => installation.Id, StringComparer.Ordinal)
                .Select(installation => new { installation.DeviceId, installation.SoftwareId }),
            ServerInstallations = world.ServerSoftwareInstallations
                .OrderBy(installation => installation.Id, StringComparer.Ordinal)
                .Select(installation => new { installation.ServerId, installation.SoftwareId }),
        };

    private static ScenarioDefinition CreateScenario(
        bool includeRepresentativeFacts,
        int representativeObservationCount,
        int representativeHistoryObservationCount,
        int companyCount,
        int managementObservationPopulationCoveragePercentage = 0,
        bool includeConfigurationManagement = false)
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
                RepresentativeManagementHistoryObservationCount = representativeHistoryObservationCount,
                ManagementObservationPopulationCoveragePercentage = managementObservationPopulationCoveragePercentage,
            },
            Applications = new ApplicationProfile
            {
                IncludeApplications = true,
                BaseApplicationCount = 6,
                IncludeLineOfBusinessApplications = true,
                IncludeSaaSApplications = true,
            },
            Cmdb = new CmdbProfile
            {
                IncludeConfigurationManagement = includeConfigurationManagement,
            },
            Companies = Enumerable.Range(1, companyCount)
                .Select(index => new ScenarioCompanyDefinition
                {
                    Name = $"Representative Manufacturing {index}",
                    Industry = "Manufacturing",
                    EmployeeCount = 120,
                    BusinessUnitCount = 2,
                    DepartmentCountPerBusinessUnit = 3,
                    TeamCountPerDepartment = 2,
                    OfficeCount = 2,
                    ServerCount = 12,
                    Countries = ["United States"],
                })
                .ToList(),
        };
    }

    private static SyntheticEnterpriseWorld GenerateManagementObservations(
        int requestedCount,
        int hostedPercentage,
        params ServerAsset[] servers)
    {
        var world = new SyntheticEnterpriseWorld();
        var company = new Company { Id = "CO-001", Name = "Management observation test" };
        world.Companies.Add(company);
        world.Servers.AddRange(servers);

        RepresentativeManagementObservationGenerator.Apply(
            world,
            company,
            new GenerationContext
            {
                Scenario = new ScenarioDefinition { Name = "Management observation test" },
                Seed = 1130,
                GeneratedAt = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
            },
            new TestIdFactory(),
            new InfrastructureProfile
            {
                RepresentativeManagementObservationCount = requestedCount,
                RepresentativeManagementHistoryObservationCount = 0,
                HostedComputeObservationPercentage = hostedPercentage,
            });

        return world;
    }

    private static ServerAsset CreateServer(
        string id,
        string? hostingLocationType,
        string? cloudProvider = null)
        => new()
        {
            Id = id,
            CompanyId = "CO-001",
            Hostname = id.ToLowerInvariant(),
            OperatingSystem = "Windows Server",
            HostingLocationType = hostingLocationType!,
            CloudProvider = cloudProvider,
        };

    private static EndpointManagementObservation[] CurrentServerObservations(SyntheticEnterpriseWorld world)
        => world.ManagementObservations
            .Where(observation => observation.IsCurrent && observation.EndpointType == "Server")
            .OrderBy(observation => observation.EndpointId, StringComparer.Ordinal)
            .ToArray();

    private static SyntheticEnterpriseWorld GeneratePopulationCoverage(
        int percentage,
        int companyCount,
        int endpointsPerCompany)
    {
        var world = CreatePopulationWorld(companyCount, endpointsPerCompany);
        var idFactory = new TestIdFactory();
        foreach (var company in world.Companies.OrderBy(company => company.Id, StringComparer.Ordinal))
        {
            RepresentativeManagementObservationGenerator.Apply(
                world,
                company,
                CreateGenerationContext(),
                idFactory,
                new InfrastructureProfile
                {
                    RepresentativeManagementObservationCount = 1,
                    RepresentativeManagementHistoryObservationCount = 0,
                    ManagementObservationPopulationCoveragePercentage = percentage,
                });
        }

        return world;
    }

    private static SyntheticEnterpriseWorld CreatePopulationWorld(int companyCount, int endpointsPerCompany)
    {
        var world = new SyntheticEnterpriseWorld();
        for (var companyIndex = 1; companyIndex <= companyCount; companyIndex++)
        {
            var company = new Company
            {
                Id = $"CO-{companyIndex:000}",
                Name = $"Population company {companyIndex}",
            };
            world.Companies.Add(company);
            for (var endpointIndex = 1; endpointIndex <= endpointsPerCompany; endpointIndex++)
            {
                world.Devices.Add(new ManagedDevice
                {
                    Id = $"DEV-{companyIndex}-{endpointIndex:000}",
                    CompanyId = company.Id,
                    Hostname = $"device-{companyIndex}-{endpointIndex:000}",
                    OperatingSystem = "Windows 11",
                    DomainJoined = true,
                });
            }
        }

        return world;
    }

    private static GenerationContext CreateGenerationContext() => new()
    {
        Scenario = new ScenarioDefinition { Name = "Population coverage contract" },
        Seed = 1130,
        GeneratedAt = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
    };

    private static string ProjectObservation(EndpointManagementObservation observation)
        => $"{observation.Id}|{observation.CompanyId}|{observation.EndpointType}|{observation.EndpointId}|{observation.ManagementProvider}";

    private static int CurrentProviderCount(
        IReadOnlyCollection<EndpointManagementObservation> observations,
        IEnumerable<(string EndpointType, string EndpointId)> endpoints,
        string provider)
    {
        var keys = endpoints
            .Select(endpoint => $"{endpoint.EndpointType}|{endpoint.EndpointId}")
            .ToHashSet(StringComparer.Ordinal);
        return observations.Count(observation =>
            observation.ManagementProvider == provider
            && keys.Contains($"{observation.EndpointType}|{observation.EndpointId}"));
    }

    private static TemporalCorrectionProjection ProjectTemporalCorrections(SyntheticEnterpriseWorld world)
        => new(
            world.RelationshipHistoryObservations
                .OrderBy(observation => observation.RelationshipType, StringComparer.Ordinal)
                .ThenBy(observation => observation.FromEntityId, StringComparer.Ordinal)
                .ThenBy(observation => observation.ToEntityId, StringComparer.Ordinal)
                .ThenBy(observation => observation.ObservedAtUtc)
                .Select(observation => string.Join('|',
                    observation.RelationshipType,
                    observation.FromEntityId,
                    observation.ToEntityId,
                    observation.LifecycleState,
                    observation.ObservedAtUtc.ToString("O"),
                    observation.RemovedAtUtc?.ToString("O") ?? string.Empty))
                .ToArray(),
            world.ManagementObservations
                .OrderBy(observation => observation.CompanyId, StringComparer.Ordinal)
                .ThenBy(observation => observation.EndpointType, StringComparer.Ordinal)
                .ThenBy(observation => observation.EndpointId, StringComparer.Ordinal)
                .ThenBy(observation => observation.ManagementProvider, StringComparer.Ordinal)
                .ThenBy(observation => observation.ObservedAtUtc)
                .Select(observation => string.Join('|',
                    observation.CompanyId,
                    observation.EndpointType,
                    observation.EndpointId,
                    observation.ManagementProvider,
                    observation.LifecycleState,
                    observation.IsCurrent,
                    observation.SupersededByObservationId,
                    observation.RegistrationState,
                    observation.DeploymentCapability,
                    observation.ObservedAtUtc.ToString("O"),
                    observation.LastCheckInAtUtc?.ToString("O") ?? string.Empty))
                .ToArray());

    private static EndpointManagementObservation[][] GetManagementHistoryGroups(SyntheticEnterpriseWorld world)
        => world.ManagementObservations
            .GroupBy(observation => new
            {
                observation.CompanyId,
                observation.EndpointType,
                observation.EndpointId,
                observation.ManagementProvider,
            })
            .Where(group => group.Any(observation => !observation.IsCurrent))
            .Select(group => group.OrderBy(observation => observation.ObservedAtUtc).ToArray())
            .ToArray();

    private static void AssertManagementHistory(
        EndpointManagementObservation[] history,
        int expectedHistoryCount)
    {
        var current = Assert.Single(history, observation => observation.IsCurrent);
        var historical = history.Where(observation => !observation.IsCurrent).ToArray();
        Assert.Equal(expectedHistoryCount, historical.Length);
        Assert.Equal("Current", current.LifecycleState);
        Assert.Null(current.SupersededByObservationId);
        Assert.All(historical, observation =>
        {
            Assert.Equal("Historical", observation.LifecycleState);
            Assert.Equal(current.Id, observation.SupersededByObservationId);
            Assert.Equal(current.CompanyId, observation.CompanyId);
            Assert.Equal(current.EndpointType, observation.EndpointType);
            Assert.Equal(current.EndpointId, observation.EndpointId);
            Assert.Equal(current.ManagementProvider, observation.ManagementProvider);
            Assert.Equal(current.RegistrationId, observation.RegistrationId);
            Assert.Equal(current.AgentInstanceId, observation.AgentInstanceId);
            Assert.True(observation.LastCheckInAtUtc < observation.ObservedAtUtc);
            Assert.True(observation.ObservedAtUtc < current.ObservedAtUtc);
            Assert.True(current.LastCheckInAtUtc > observation.LastCheckInAtUtc);
            Assert.True(current.LastCheckInAtUtc <= current.ObservedAtUtc);
            Assert.InRange(current.ObservedAtUtc - observation.ObservedAtUtc, TimeSpan.FromDays(21), TimeSpan.FromDays(27));
            Assert.InRange(observation.ObservedAtUtc - observation.LastCheckInAtUtc!.Value, TimeSpan.FromDays(14), TimeSpan.FromDays(18));
        });
    }

    private static EndpointManagementObservation CreateCurrentObservation(
        string id,
        DateTimeOffset observedAtUtc)
        => new()
        {
            Id = id,
            CompanyId = "CO-001",
            EndpointType = "Device",
            EndpointId = "DEV-001",
            ObservationKind = "Registration",
            ManagementProvider = "RepresentativeManagement",
            AgentInstanceId = "agent-DEV-001",
            RegistrationId = "registration-DEV-001",
            RegistrationState = "Registered",
            ConfigurationCapability = "Supported",
            DeploymentCapability = "Supported",
            UpdateCapability = "Supported",
            ObservedAtUtc = observedAtUtc,
            LastCheckInAtUtc = observedAtUtc,
        };

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

    private sealed record TemporalCorrectionProjection(
        string[] Relationships,
        string[] Management);
}
