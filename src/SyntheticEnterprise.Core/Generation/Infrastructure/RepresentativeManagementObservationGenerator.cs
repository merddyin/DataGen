namespace SyntheticEnterprise.Core.Generation.Infrastructure;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.Generation;

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

    private static readonly ManagementProfile WindowsWorkstationProfile =
        new("MicrosoftIntune", "Microsoft Intune Management Extension", "Registered", "Supported", 1, 0.97m);
    private static readonly ManagementProfile MacWorkstationProfile =
        new("Jamf", "Jamf Pro Management Agent", "Registered", "Supported", 1, 0.96m);
    private static readonly ManagementProfile WindowsServerProfile =
        new("ConfigurationManager", "Configuration Manager Client", "Registered", "Supported", 2, 0.94m);
    private static readonly ManagementProfile LinuxEndpointProfile =
        new("Ansible", "Ansible Automation Platform", "Registered", "Supported", 2, 0.92m);
    private static readonly ManagementProfile UnmanagedProfile =
        new("None", null, "NotRegistered", "Unsupported", null, 0.35m);

    public static void Apply(
        SyntheticEnterpriseWorld world,
        Company company,
        GenerationContext context,
        IIdFactory idFactory,
        InfrastructureProfile configuration)
    {
        var observedAt = context.GeneratedAt;
        var requestedCount = Math.Max(0, configuration.RepresentativeManagementObservationCount);
        var historyBudget = Math.Max(0, configuration.RepresentativeManagementHistoryObservationCount);
        if (requestedCount == 0)
        {
            return;
        }

        var populationCoveragePercentage = Math.Clamp(
            configuration.ManagementObservationPopulationCoveragePercentage,
            0,
            100);
        if (populationCoveragePercentage > 0)
        {
            ApplyPopulationCoverage(
                world,
                company,
                context,
                idFactory,
                populationCoveragePercentage);
            AddStaleManagementHistory(world, company.Id, context, idFactory, historyBudget);
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
        var hostedTarget = serverTarget == 0 || configuration.HostedComputeObservationPercentage <= 0
            ? 0
            : Math.Clamp(
                (int)Math.Round(
                    serverTarget * Math.Clamp(configuration.HostedComputeObservationPercentage, 0, 100) / 100m,
                    MidpointRounding.AwayFromZero),
                1,
                serverTarget);

        var software = EnsureManagementSoftware(
            world,
            idFactory,
            includePopulationCoverageSoftware: false);
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

        var selectedServers = SelectRepresentativeServers(companyServers, serverTarget, hostedTarget);
        var hostedObservationIndex = 0;
        for (var index = 0; index < selectedServers.Length; index++)
        {
            var server = selectedServers[index];
            var hosted = IsHostedServer(server);
            var needsOnlyCurrentForHistory = requestedCount == 1 && historyBudget > 0;
            var profile = hosted && hostedObservationIndex == 0 && !needsOnlyCurrentForHistory
                ? ManagementProfiles[4]
                : ManagementProfiles[(deviceTarget + index) % ManagementProfiles.Length];
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
                hosted ? server.CloudProvider : null,
                hosted && hostedObservationIndex == 0 ? "Supported" : hosted ? "Unknown" : "Unavailable",
                profile,
                observedAt,
                software);

            if (hosted)
            {
                hostedObservationIndex++;
            }
        }

        AddStaleManagementHistory(world, company.Id, context, idFactory, historyBudget);
    }

    private static void ApplyPopulationCoverage(
        SyntheticEnterpriseWorld world,
        Company company,
        GenerationContext context,
        IIdFactory idFactory,
        int coveragePercentage)
    {
        var software = EnsureManagementSoftware(
            world,
            idFactory,
            includePopulationCoverageSoftware: true);
        var seed = (context.Seed ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var endpoints = new List<PopulationEndpoint>();
        endpoints.AddRange(world.Devices
            .Where(device => device.CompanyId == company.Id)
            .Select(device => new PopulationEndpoint(
                "Device",
                device.Id,
                device.DirectoryAccountId ?? device.OnPremDirectoryAccountId ?? device.CloudDirectoryAccountId,
                device.OperatingSystem,
                device.DomainJoined,
                device.CloudDirectoryAccountId,
                false,
                null)));
        endpoints.AddRange(world.Servers
            .Where(server => server.CompanyId == company.Id)
            .Select(server => new PopulationEndpoint(
                "Server",
                server.Id,
                server.DirectoryAccountId ?? server.OnPremDirectoryAccountId ?? server.CloudDirectoryAccountId,
                server.OperatingSystem,
                server.DomainJoined,
                server.CloudDirectoryAccountId,
                IsHostedServer(server),
                IsHostedServer(server) ? server.CloudProvider : null)));

        foreach (var cohort in endpoints
                     .GroupBy(ResolveCohort, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ranked = cohort
                .OrderBy(endpoint => StableHash.GetIndex(
                    "endpoint-management-population-coverage",
                    int.MaxValue,
                    seed,
                    company.Id,
                    cohort.Key,
                    endpoint.EndpointType,
                    endpoint.EndpointId))
                .ThenBy(endpoint => endpoint.EndpointType, StringComparer.Ordinal)
                .ThenBy(endpoint => endpoint.EndpointId, StringComparer.Ordinal)
                .ToArray();
            var selectedCount = coveragePercentage == 100
                ? ranked.Length
                : Math.Clamp(
                    (int)Math.Ceiling(ranked.Length * coveragePercentage / 100m),
                    1,
                    ranked.Length);
            var selected = ranked.Take(selectedCount).ToArray();
            var hostedFallbackIds = selected
                .Where(endpoint => endpoint.IsHosted)
                .OrderBy(endpoint => StableHash.GetIndex(
                    "endpoint-management-hosted-fallback",
                    int.MaxValue,
                    seed,
                    company.Id,
                    endpoint.EndpointType,
                    endpoint.EndpointId))
                .ThenBy(endpoint => endpoint.EndpointId, StringComparer.Ordinal)
                .Take(OutlierCount(selected.Count(endpoint => endpoint.IsHosted), 2))
                .Select(endpoint => endpoint.Key)
                .ToHashSet(StringComparer.Ordinal);
            var missingCount = OutlierCount(selected.Length, 1);
            var staleCount = OutlierCount(selected.Length, 2);
            var alternateCount = OutlierCount(selected.Length, 2);

            for (var index = 0; index < selected.Length; index++)
            {
                var endpoint = selected[index];
                var hostedFallback = hostedFallbackIds.Contains(endpoint.Key);
                var profile = ResolvePopulationProfile(
                    cohort.Key,
                    index,
                    missingCount,
                    staleCount,
                    alternateCount,
                    hostedFallback);
                var joinStateIndex = StableHash.GetIndex(
                    "endpoint-management-join-state",
                    4,
                    seed,
                    company.Id,
                    endpoint.EndpointType,
                    endpoint.EndpointId);
                AddObservation(
                    world,
                    idFactory,
                    company.Id,
                    endpoint.EndpointType,
                    endpoint.EndpointId,
                    endpoint.DeviceAccountId,
                    endpoint.OperatingSystem,
                    ResolveJoinState(endpoint.DomainJoined, endpoint.CloudDirectoryAccountId, joinStateIndex),
                    cohort.Key,
                    endpoint.IsHosted ? "HostedCompute" : "NonHosted",
                    endpoint.HostingProvider,
                    hostedFallback ? "Supported" : endpoint.IsHosted ? "Unknown" : "Unavailable",
                    profile,
                    context.GeneratedAt,
                    software);
            }
        }
    }

    private static ManagementProfile ResolvePopulationProfile(
        string cohort,
        int rank,
        int missingCount,
        int staleCount,
        int alternateCount,
        bool hostedFallback)
    {
        if (hostedFallback || rank < missingCount)
        {
            return UnmanagedProfile;
        }

        var dominant = ResolveDominantProfile(cohort);
        if (rank < missingCount + staleCount)
        {
            return dominant with { CheckInAgeDays = 35, Confidence = 0.70m };
        }

        if (rank < missingCount + staleCount + alternateCount)
        {
            return ResolveAlternateProfile(cohort);
        }

        return dominant;
    }

    private static ManagementProfile ResolveDominantProfile(string cohort) => cohort switch
    {
        "WindowsWorkstation" => WindowsWorkstationProfile,
        "MacWorkstation" => MacWorkstationProfile,
        "WindowsServer" => WindowsServerProfile,
        _ => LinuxEndpointProfile,
    };

    private static ManagementProfile ResolveAlternateProfile(string cohort) => cohort switch
    {
        "WindowsWorkstation" => new("ConfigurationManager", "Configuration Manager Client", "Registered", "Supported", 2, 0.90m),
        "MacWorkstation" => new("Rmm", "Remote Monitoring Agent", "Registered", "Supported", 3, 0.88m),
        "WindowsServer" => new("BigFix", "BigFix Client", "Registered", "Supported", 3, 0.87m),
        _ => new("Puppet", "Puppet Agent", "Registered", "Supported", 3, 0.88m),
    };

    private static string ResolveCohort(PopulationEndpoint endpoint)
    {
        var family = OperatingSystemFamily(endpoint.OperatingSystem);
        return endpoint.EndpointType == "Server"
            ? family == "Windows" ? "WindowsServer" : "LinuxServer"
            : family == "Windows" ? "WindowsWorkstation" : family == "macOS" ? "MacWorkstation" : "LinuxWorkstation";
    }

    private static int OutlierCount(int population, int percentage)
    {
        if (population <= 0 || percentage <= 0)
        {
            return 0;
        }

        return (int)Math.Floor(population * percentage / 100m);
    }

    private static ServerAsset[] SelectRepresentativeServers(
        IReadOnlyList<ServerAsset> servers,
        int serverTarget,
        int hostedTarget)
    {
        var hostedServers = servers.Where(IsHostedServer).ToArray();
        var nonHostedServers = servers.Where(server => !IsHostedServer(server)).ToArray();
        var selected = new List<ServerAsset>(serverTarget);
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);

        void AddUnique(IEnumerable<ServerAsset> candidates, int targetCount)
        {
            foreach (var candidate in candidates)
            {
                if (selected.Count >= targetCount)
                {
                    return;
                }

                if (selectedIds.Add(candidate.Id))
                {
                    selected.Add(candidate);
                }
            }
        }

        var diverseHostedServers = hostedServers
            .GroupBy(server => server.CloudProvider ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(server => server.Id, StringComparer.Ordinal);
        AddUnique(diverseHostedServers, hostedTarget);
        AddUnique(hostedServers, hostedTarget);

        var nonHostedTarget = serverTarget - hostedTarget;
        AddUnique(nonHostedServers, selected.Count + nonHostedTarget);

        AddUnique(hostedServers, serverTarget);
        AddUnique(nonHostedServers, serverTarget);
        return selected.ToArray();
    }

    private static bool IsHostedServer(ServerAsset server)
    {
        if (string.Equals(server.HostingLocationType, "Cloud", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(server.HostingLocationType, "OnPremises", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(server.CloudProvider);
    }

    internal static void AddStaleManagementHistory(
        SyntheticEnterpriseWorld world,
        string companyId,
        GenerationContext context,
        IIdFactory idFactory,
        int historyBudget)
    {
        if (historyBudget <= 0)
        {
            return;
        }

        var currentObservations = world.ManagementObservations
            .Where(observation => observation.CompanyId == companyId
                && observation.IsCurrent
                && observation.LifecycleState == "Current"
                && observation.SupersededByObservationId is null
                && observation.RegistrationState == "Registered"
                && observation.DeploymentCapability == "Supported")
            .OrderBy(observation => observation.EndpointType, StringComparer.Ordinal)
            .ThenBy(observation => observation.EndpointId, StringComparer.Ordinal)
            .ThenBy(observation => observation.ManagementProvider, StringComparer.Ordinal)
            .Take(historyBudget)
            .ToArray();
        var seedComponent = (context.Seed ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (var currentObservation in currentObservations)
        {
            var currentObservedAtUtc = currentObservation.ObservedAtUtc.ToUniversalTime();
            if (currentObservedAtUtc.UtcTicks < DateTimeOffset.MinValue.UtcTicks + 2)
            {
                continue;
            }

            var historyScope = new[]
            {
                seedComponent,
                currentObservation.CompanyId,
                currentObservation.EndpointType,
                currentObservation.EndpointId,
                currentObservation.ManagementProvider,
            };
            var historicalAgeDays = 21 + StableHash.GetIndex(
                "representative-management-history-age",
                7,
                historyScope);
            var staleCheckInAgeDays = 14 + StableHash.GetIndex(
                "representative-management-history-check-in",
                5,
                historyScope);
            var historicalObservedAt = SubtractClamped(
                currentObservedAtUtc,
                TimeSpan.FromDays(historicalAgeDays),
                DateTimeOffset.MinValue.AddTicks(1));
            var historicalLastCheckInAt = SubtractClamped(
                historicalObservedAt,
                TimeSpan.FromDays(staleCheckInAgeDays),
                DateTimeOffset.MinValue);

            world.ManagementObservations.Add(currentObservation with
            {
                Id = idFactory.Next("MGO"),
                LifecycleState = "Historical",
                IsCurrent = false,
                SupersededByObservationId = currentObservation.Id,
                RegistrationState = "Unreachable",
                ConfigurationCapability = "Unknown",
                DeploymentCapability = "Unknown",
                UpdateCapability = "Unknown",
                ObservedAtUtc = historicalObservedAt,
                LastCheckInAtUtc = historicalLastCheckInAt,
                Confidence = Math.Min(currentObservation.Confidence, 0.55m),
            });
        }
    }

    private static DateTimeOffset SubtractClamped(
        DateTimeOffset value,
        TimeSpan amount,
        DateTimeOffset minimum)
    {
        var valueTicks = value.UtcTicks;
        var minimumTicks = minimum.UtcTicks;
        var resultTicks = valueTicks - minimumTicks < amount.Ticks
            ? minimumTicks
            : valueTicks - amount.Ticks;
        return new DateTimeOffset(resultTicks, TimeSpan.Zero);
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
            LifecycleState = "Current",
            IsCurrent = true,
            SupersededByObservationId = null,
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
            LastCheckInAtUtc = profile.CheckInAgeDays is int age
                ? SubtractClamped(observedAt, TimeSpan.FromDays(age), DateTimeOffset.MinValue)
                : null,
            ExpectedCheckInIntervalSeconds = 86400,
            Confidence = profile.Confidence,
        });
    }

    private static Dictionary<string, string> EnsureManagementSoftware(
        SyntheticEnterpriseWorld world,
        IIdFactory idFactory,
        bool includePopulationCoverageSoftware)
    {
        var definitions = new List<(string Name, string Category, string Vendor, string Version)>
        {
            ("Microsoft Intune Management Extension", "Management", "Microsoft", "1.82"),
            ("Configuration Manager Client", "Management", "Microsoft", "5.00"),
            ("BigFix Client", "Management", "HCL", "11.0"),
            ("Remote Monitoring Agent", "Management", "Representative Vendor", "4.8"),
            ("Ansible Automation Platform", "Automation", "Red Hat", "2.5"),
        };
        if (includePopulationCoverageSoftware)
        {
            definitions.Add(("Jamf Pro Management Agent", "Management", "Jamf", "11.8"));
            definitions.Add(("Puppet Agent", "ConfigurationManagement", "Perforce", "8.7"));
        }

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

    private static void AddInstallation(
        SyntheticEnterpriseWorld world,
        IIdFactory idFactory,
        string endpointType,
        string endpointId,
        string softwareId)
    {
        if (endpointType == "Server")
        {
            if (world.ServerSoftwareInstallations.Any(installation =>
                    installation.ServerId == endpointId
                    && installation.SoftwareId == softwareId))
            {
                return;
            }

            world.ServerSoftwareInstallations.Add(new ServerSoftwareInstallation
            {
                Id = idFactory.Next("SSI"),
                ServerId = endpointId,
                SoftwareId = softwareId,
            });
            return;
        }

        if (world.DeviceSoftwareInstallations.Any(installation =>
                installation.DeviceId == endpointId
                && installation.SoftwareId == softwareId))
        {
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

    private sealed record PopulationEndpoint(
        string EndpointType,
        string EndpointId,
        string? DeviceAccountId,
        string OperatingSystem,
        bool DomainJoined,
        string? CloudDirectoryAccountId,
        bool IsHosted,
        string? HostingProvider)
    {
        public string Key => $"{EndpointType}|{EndpointId}";
    }
}
