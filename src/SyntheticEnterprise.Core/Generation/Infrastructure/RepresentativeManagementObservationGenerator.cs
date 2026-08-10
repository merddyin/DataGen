namespace SyntheticEnterprise.Core.Generation.Infrastructure;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

internal static class RepresentativeManagementObservationGenerator
{
    private static readonly ManagementProfile[] ManagementProfiles =
    [
        new("MicrosoftIntune", "Microsoft Intune Management Extension", "Registered", "Supported", 1, 0.97m),
        new("ConfigurationManager", "Configuration Manager Client", "Registered", "Supported", 3, 0.94m),
        new("BigFix", "BigFix Client", "Unreachable", "Supported", 90, 0.62m),
        new("Rmm", "Remote Monitoring Agent", "Registered", "Supported", 7, 0.88m),
        new("None", null, "NotRegistered", "Unsupported", null, 0.35m),
        new("Ansible", "Ansible Automation Platform", "Registered", "Supported", 5, 0.91m),
    ];

    public static void Apply(
        SyntheticEnterpriseWorld world,
        Company company,
        GenerationContext context,
        IIdFactory idFactory,
        InfrastructureProfile configuration)
    {
        var observedAt = context.GeneratedAt;
        var requestedCount = Math.Max(0, configuration.RepresentativeManagementObservationCount);
        if (requestedCount == 0)
        {
            return;
        }

        var companyDevices = world.Devices
            .Where(device => device.CompanyId == company.Id)
            .OrderBy(device => device.Id, StringComparer.Ordinal)
            .ToArray();
        var companyServers = world.Servers
            .Where(server => server.CompanyId == company.Id)
            .OrderBy(server => server.Id, StringComparer.Ordinal)
            .ToArray();
        var serverTarget = Math.Min(companyServers.Length, Math.Max(1, requestedCount / 3));
        var deviceTarget = Math.Min(companyDevices.Length, requestedCount - serverTarget);
        serverTarget = Math.Min(companyServers.Length, requestedCount - deviceTarget);
        var hostedTarget = configuration.HostedComputeObservationPercentage <= 0
            ? 0
            : Math.Clamp(
                (int)Math.Round(
                    serverTarget * Math.Clamp(configuration.HostedComputeObservationPercentage, 0, 100) / 100m,
                    MidpointRounding.AwayFromZero),
                1,
                serverTarget);

        var software = EnsureManagementSoftware(world, idFactory);
        for (var index = 0; index < deviceTarget; index++)
        {
            var device = companyDevices[index];
            var profile = ManagementProfiles[index % ManagementProfiles.Length];
            AddObservation(
                world,
                idFactory,
                company.Id,
                "Device",
                device.Id,
                device.DirectoryAccountId ?? device.OnPremDirectoryAccountId ?? device.CloudDirectoryAccountId,
                device.OperatingSystem,
                ResolveJoinState(device.DomainJoined, device.CloudDirectoryAccountId, index),
                $"workstation-inventory-{(index % 2) + 1}",
                "NonHosted",
                null,
                "Unknown",
                profile,
                observedAt,
                software);
        }

        for (var index = 0; index < serverTarget; index++)
        {
            var server = companyServers[index];
            var hosted = index < hostedTarget;
            var profile = hosted && index == 0
                ? ManagementProfiles[4]
                : ManagementProfiles[(deviceTarget + index) % ManagementProfiles.Length];
            var hostingProvider = hosted ? HostedProvider(index) : null;
            AddObservation(
                world,
                idFactory,
                company.Id,
                "Server",
                server.Id,
                server.DirectoryAccountId ?? server.OnPremDirectoryAccountId ?? server.CloudDirectoryAccountId,
                server.OperatingSystem,
                ResolveJoinState(server.DomainJoined, server.CloudDirectoryAccountId, deviceTarget + index),
                hosted ? "hosted-server-inventory" : "datacenter-server-inventory",
                hosted ? "HostedCompute" : "NonHosted",
                hostingProvider,
                hosted && index == 0 ? "Supported" : hosted ? "Unknown" : "Unavailable",
                profile,
                observedAt,
                software);
        }

    }

    private static void AddObservation(
        SyntheticEnterpriseWorld world,
        IIdFactory idFactory,
        string companyId,
        string endpointType,
        string endpointId,
        string? deviceAccountId,
        string operatingSystem,
        string joinState,
        string cohort,
        string hostingEnvironmentKind,
        string? hostingProvider,
        string outOfBandGuestDeploymentCapability,
        ManagementProfile profile,
        DateTimeOffset observedAt,
        IReadOnlyDictionary<string, string> software)
    {
        var agentSoftwareId = profile.AgentName is null ? null : software[profile.AgentName];
        if (agentSoftwareId is not null)
        {
            AddInstallation(world, idFactory, endpointType, endpointId, agentSoftwareId);
        }

        world.ManagementObservations.Add(new EndpointManagementObservation
        {
            Id = idFactory.Next("MGO"),
            CompanyId = companyId,
            EndpointType = endpointType,
            EndpointId = endpointId,
            DeviceAccountId = deviceAccountId,
            ObservationKind = profile.RegistrationState == "Registered" ? "Registration" : "EndpointObservation",
            SourceKind = "GeneratedInventory",
            ManagementProvider = profile.Provider,
            AgentSoftwareId = agentSoftwareId,
            AgentInstanceId = agentSoftwareId is null ? null : $"agent-{endpointId}",
            RegistrationId = profile.RegistrationState == "Registered" ? $"registration-{endpointId}" : null,
            RegistrationState = profile.RegistrationState,
            JoinState = joinState,
            ConfigurationCapability = profile.Capability,
            DeploymentCapability = profile.Capability,
            UpdateCapability = profile.Capability == "Supported" ? "Supported" : "Unknown",
            OperatingSystemFamily = OperatingSystemFamily(operatingSystem),
            Cohort = cohort,
            HostingEnvironmentKind = hostingEnvironmentKind,
            HostingProvider = hostingProvider,
            OutOfBandGuestDeploymentCapability = outOfBandGuestDeploymentCapability,
            ObservedAtUtc = observedAt,
            LastCheckInAtUtc = profile.CheckInAgeDays is int age ? observedAt.AddDays(-age) : null,
            ExpectedCheckInIntervalSeconds = 86400,
            Confidence = profile.Confidence,
        });
    }

    private static Dictionary<string, string> EnsureManagementSoftware(
        SyntheticEnterpriseWorld world,
        IIdFactory idFactory)
    {
        var definitions = new[]
        {
            ("Microsoft Intune Management Extension", "Management", "Microsoft", "1.82"),
            ("Configuration Manager Client", "Management", "Microsoft", "5.00"),
            ("BigFix Client", "Management", "HCL", "11.0"),
            ("Remote Monitoring Agent", "Management", "Representative Vendor", "4.8"),
            ("Ansible Automation Platform", "Automation", "Red Hat", "2.5"),
        };
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var package = world.SoftwarePackages.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, definition.Item1, StringComparison.Ordinal));
            if (package is null)
            {
                package = new SoftwarePackage
                {
                    Id = idFactory.Next("SW"),
                    Name = definition.Item1,
                    Category = definition.Item2,
                    Vendor = definition.Item3,
                    Version = definition.Item4,
                };
                world.SoftwarePackages.Add(package);
            }

            ids[definition.Item1] = package.Id;
        }

        return ids;
    }

    internal static void AddRepresentativeHistory(
        SyntheticEnterpriseWorld world,
        Company company,
        DateTimeOffset observedAt,
        IIdFactory idFactory)
    {
        var applications = world.Applications
            .Where(application => application.CompanyId == company.Id)
            .ToDictionary(application => application.Id, StringComparer.Ordinal);
        var services = world.ApplicationServices
            .Where(service => service.CompanyId == company.Id)
            .ToDictionary(service => service.Id, StringComparer.Ordinal);
        var servers = world.Servers
            .Where(server => server.CompanyId == company.Id)
            .ToDictionary(server => server.Id, StringComparer.Ordinal);
        var installedServerIds = world.ServerSoftwareInstallations
            .Select(installation => installation.ServerId)
            .ToHashSet(StringComparer.Ordinal);
        var candidate = world.ApplicationServiceHostings
            .Where(hosting => hosting.CompanyId == company.Id
                              && string.Equals(hosting.HostType, "Server", StringComparison.OrdinalIgnoreCase)
                              && !string.IsNullOrWhiteSpace(hosting.HostId)
                              && services.TryGetValue(hosting.ApplicationServiceId, out _)
                              && servers.ContainsKey(hosting.HostId)
                              && installedServerIds.Contains(hosting.HostId))
            .Select(hosting => new
            {
                Hosting = hosting,
                Application = applications.GetValueOrDefault(services[hosting.ApplicationServiceId].ApplicationId),
                Server = servers[hosting.HostId!]
            })
            .Where(item => item.Application is not null)
            .OrderBy(item => item.Application!.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Server.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Hosting.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (candidate?.Application is null)
        {
            return;
        }

        AddRelationshipHistory("InstalledOn", "applications", candidate.Application.Id, "servers", candidate.Server.Id,
            "ConfigurationManagement", "An inventory snapshot recorded this hosted application.", observedAt.AddDays(-7), null, "Active");
        AddRelationshipHistory("InstalledOn", "applications", candidate.Application.Id, "servers", candidate.Server.Id,
            "ConfigurationManagement", "A prior inventory snapshot recorded this hosted application.", observedAt.AddDays(-45), observedAt.AddDays(-20), "Removed");

        var owner = world.People
            .Where(person => person.CompanyId == company.Id
                             && person.DepartmentId == candidate.Application.OwnerDepartmentId)
            .OrderBy(person => person.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (owner is null)
        {
            return;
        }

        AddRelationshipHistory("Owns", "people", owner.Id, "applications", candidate.Application.Id,
            "ServiceCatalog", "The current service catalog names this owner.", observedAt.AddDays(-7), null, "Active");
        AddRelationshipHistory("Owns", "people", owner.Id, "applications", candidate.Application.Id,
            "ServiceCatalog", "A prior service catalog named this owner.", observedAt.AddDays(-90), observedAt.AddDays(-30), "Removed");

        void AddRelationshipHistory(
            string relationshipType,
            string fromArtifact,
            string fromEntityId,
            string toArtifact,
            string toEntityId,
            string sourceSystem,
            string detail,
            DateTimeOffset observedAtUtc,
            DateTimeOffset? removedAtUtc,
            string lifecycleState) =>
            world.RelationshipHistoryObservations.Add(new RelationshipHistoryObservation
            {
                Id = idFactory.Next("RHO"),
                CompanyId = company.Id,
                RelationshipType = relationshipType,
                FromArtifact = fromArtifact,
                FromEntityType = relationshipType == "Owns" ? "Person" : "Application",
                FromEntityId = fromEntityId,
                ToArtifact = toArtifact,
                ToEntityType = relationshipType == "Owns" ? "Application" : "Server",
                ToEntityId = toEntityId,
                LifecycleState = lifecycleState,
                SourceSystem = sourceSystem,
                ObservedAtUtc = observedAtUtc,
                RemovedAtUtc = removedAtUtc,
                Detail = detail,
            });
    }

    private static string ResolveJoinState(bool domainJoined, string? cloudDirectoryAccountId, int index) =>
        domainJoined && !string.IsNullOrWhiteSpace(cloudDirectoryAccountId) ? "HybridJoined" :
        domainJoined && index % 4 == 0 ? "HybridJoined" :
        domainJoined ? "DomainJoined" :
        !string.IsNullOrWhiteSpace(cloudDirectoryAccountId) ? "CloudJoined" :
        "Workgroup";

    private static string OperatingSystemFamily(string operatingSystem) =>
        operatingSystem.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
        operatingSystem.StartsWith("macOS", StringComparison.OrdinalIgnoreCase) ? "macOS" :
        "Linux";

    private static string HostedProvider(int index) => (index % 3) switch
    {
        0 => "Azure",
        1 => "AWS",
        _ => "PrivateCloud",
    };

    private static void AddInstallation(
        SyntheticEnterpriseWorld world,
        IIdFactory idFactory,
        string endpointType,
        string endpointId,
        string softwareId)
    {
        if (endpointType == "Server")
        {
            world.ServerSoftwareInstallations.Add(new ServerSoftwareInstallation
            {
                Id = idFactory.Next("SSI"),
                ServerId = endpointId,
                SoftwareId = softwareId,
            });
            return;
        }

        world.DeviceSoftwareInstallations.Add(new DeviceSoftwareInstallation
        {
            Id = idFactory.Next("DSI"),
            DeviceId = endpointId,
            SoftwareId = softwareId,
        });
    }

    private sealed record ManagementProfile(
        string Provider,
        string? AgentName,
        string RegistrationState,
        string Capability,
        int? CheckInAgeDays,
        decimal Confidence);
}
