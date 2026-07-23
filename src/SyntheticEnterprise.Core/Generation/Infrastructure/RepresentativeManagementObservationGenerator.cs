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

        AddRepresentativeHistory(world, company, observedAt, idFactory);
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

    private static void AddRepresentativeHistory(
        SyntheticEnterpriseWorld world,
        Company company,
        DateTimeOffset observedAt,
        IIdFactory idFactory)
    {
        var server = world.Servers
            .Where(candidate => candidate.CompanyId == company.Id)
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        var software = world.SoftwarePackages
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (server is not null && software is not null)
        {
            world.RelationshipHistoryObservations.Add(new RelationshipHistoryObservation
            {
                Id = idFactory.Next("RHO"),
                CompanyId = company.Id,
                RelationshipType = "InstalledOn",
                FromArtifact = "software_packages",
                FromEntityType = "Application",
                FromEntityId = software.Id,
                ToArtifact = "servers",
                ToEntityType = "Server",
                ToEntityId = server.Id,
                LifecycleState = "Removed",
                SourceSystem = "ConfigurationManagement",
                ObservedAtUtc = observedAt.AddDays(-45),
                RemovedAtUtc = observedAt.AddDays(-20),
                Detail = "A prior inventory snapshot contained this installation.",
            });
        }

        var owner = world.People
            .Where(candidate => candidate.CompanyId == company.Id)
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (owner is not null && software is not null)
        {
            world.RelationshipHistoryObservations.Add(new RelationshipHistoryObservation
            {
                Id = idFactory.Next("RHO"),
                CompanyId = company.Id,
                RelationshipType = "Owns",
                FromArtifact = "people",
                FromEntityType = "Person",
                FromEntityId = owner.Id,
                ToArtifact = "software_packages",
                ToEntityType = "Application",
                ToEntityId = software.Id,
                LifecycleState = "Removed",
                SourceSystem = "ServiceCatalog",
                ObservedAtUtc = observedAt.AddDays(-90),
                RemovedAtUtc = observedAt.AddDays(-30),
                Detail = "A prior service inventory named this owner.",
            });
        }
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
