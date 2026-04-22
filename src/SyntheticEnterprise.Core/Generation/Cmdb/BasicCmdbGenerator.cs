namespace SyntheticEnterprise.Core.Generation.Cmdb;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicCmdbGenerator : ICmdbGenerator
{
    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;
    private readonly IClock _clock;

    public BasicCmdbGenerator(IIdFactory idFactory, IRandomSource randomSource, IClock clock)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
        _clock = clock;
    }

    public void GenerateConfigurationManagement(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        _ = catalogs;
        if (!context.Scenario.Cmdb.IncludeConfigurationManagement)
        {
            return;
        }

        if (world.ConfigurationItems.Count > 0 || world.CmdbSourceRecords.Count > 0)
        {
            return;
        }

        var ciBySourceKey = new Dictionary<string, ConfigurationItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var company in world.Companies)
        {
            var companyContext = BuildCompanyContext(world, company);
            var deviationProfile = ResolveDeviationProfile(context.Scenario);
            ProjectCanonicalConfigurationItems(world, companyContext, context.Scenario.Cmdb, ciBySourceKey);
            ProjectCanonicalRelationships(world, companyContext, ciBySourceKey, context.Scenario.Cmdb);
            GenerateSourceViews(world, companyContext, context.Scenario.Cmdb, deviationProfile);
        }
    }

    private CompanyContext BuildCompanyContext(SyntheticEnterpriseWorld world, Company company)
    {
        var departments = world.Departments.Where(department => department.CompanyId == company.Id).ToList();
        var teams = world.Teams.Where(team => team.CompanyId == company.Id).ToList();
        var people = world.People.Where(person => person.CompanyId == company.Id).ToList();
        var offices = world.Offices.Where(office => office.CompanyId == company.Id).ToList();
        var businessUnits = world.BusinessUnits.Where(unit => unit.CompanyId == company.Id).ToList();

        return new CompanyContext(
            Company: company,
            Departments: departments,
            DepartmentsById: departments.ToDictionary(item => item.Id, item => item, StringComparer.OrdinalIgnoreCase),
            Teams: teams,
            TeamsById: teams.ToDictionary(item => item.Id, item => item, StringComparer.OrdinalIgnoreCase),
            TeamsByDepartmentId: teams
                .GroupBy(item => item.DepartmentId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase),
            People: people,
            PeopleById: people.ToDictionary(item => item.Id, item => item, StringComparer.OrdinalIgnoreCase),
            PeopleByDepartmentId: people
                .GroupBy(item => item.DepartmentId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase),
            Offices: offices,
            OfficesById: offices.ToDictionary(item => item.Id, item => item, StringComparer.OrdinalIgnoreCase),
            BusinessUnitsById: businessUnits.ToDictionary(item => item.Id, item => item, StringComparer.OrdinalIgnoreCase));
    }

    private void ProjectCanonicalConfigurationItems(
        SyntheticEnterpriseWorld world,
        CompanyContext companyContext,
        CmdbProfile profile,
        IDictionary<string, ConfigurationItem> ciBySourceKey)
    {
        foreach (var store in world.IdentityStores.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "IdentityStore",
                store.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = store.CompanyId,
                    CiKey = $"identity-store:{store.Id}",
                    Name = store.Name,
                    DisplayName = store.Name,
                    CiType = "Platform",
                    CiClass = "DirectoryService",
                    SourceEntityType = "IdentityStore",
                    SourceEntityId = store.Id,
                    Vendor = store.Provider,
                    Manufacturer = store.Provider,
                    Environment = store.Environment,
                    OperationalStatus = "Active",
                    LifecycleStatus = "InService",
                    BusinessOwnerPersonId = ResolvePlatformBusinessOwner(companyContext),
                    TechnicalOwnerPersonId = ResolvePlatformTechnicalOwner(companyContext),
                    SupportTeamId = ResolvePlatformSupportTeam(companyContext),
                    OwningDepartmentId = ResolvePlatformDepartmentId(companyContext),
                    OwningLobId = ResolvePlatformBusinessUnitId(companyContext),
                    ServiceTier = "Tier1",
                    ServiceClassification = "IdentityPlatform",
                    BusinessCriticality = "High",
                    MaintenanceWindow = ResolveMaintenanceWindow(store.Environment, ResolveTimeZone(companyContext, null), "Platform"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(5, 45))
                });
        }

        if (profile.IncludeCloudServices)
        {
            foreach (var tenant in world.CloudTenants.Where(item => item.CompanyId == companyContext.Company.Id))
            {
                AddConfigurationItem(
                    world,
                    ciBySourceKey,
                    "CloudTenant",
                    tenant.Id,
                    new ConfigurationItem
                    {
                        Id = _idFactory.Next("CI"),
                        CompanyId = tenant.CompanyId,
                        CiKey = $"cloud-tenant:{tenant.Id}",
                        Name = tenant.Name,
                        DisplayName = tenant.Name,
                        CiType = "Platform",
                        CiClass = "CloudService",
                        SourceEntityType = "CloudTenant",
                        SourceEntityId = tenant.Id,
                        Vendor = tenant.Provider,
                        Manufacturer = tenant.Provider,
                        Environment = tenant.Environment,
                        OperationalStatus = "Active",
                        LifecycleStatus = "InService",
                        BusinessOwnerPersonId = ResolveBusinessOwnerPersonId(
                            companyContext,
                            companyContext.DepartmentsById.GetValueOrDefault(tenant.AdminDepartmentId)),
                        TechnicalOwnerPersonId = ResolveTechnicalOwnerPersonId(
                            companyContext,
                            companyContext.DepartmentsById.GetValueOrDefault(tenant.AdminDepartmentId),
                            null),
                        SupportTeamId = ResolveSupportTeamId(
                            companyContext,
                            companyContext.DepartmentsById.GetValueOrDefault(tenant.AdminDepartmentId),
                            null),
                        OwningDepartmentId = tenant.AdminDepartmentId,
                        OwningLobId = companyContext.DepartmentsById.GetValueOrDefault(tenant.AdminDepartmentId)?.BusinessUnitId,
                        ServiceTier = "Tier1",
                        ServiceClassification = "CloudPlatform",
                        BusinessCriticality = "High",
                        MaintenanceWindow = ResolveMaintenanceWindow(tenant.Environment, ResolveTimeZone(companyContext, null), "Platform"),
                        LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(7, 60))
                    });
            }
        }

        foreach (var application in world.Applications.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var department = companyContext.DepartmentsById.GetValueOrDefault(application.OwnerDepartmentId);
            var ciClass = ResolveApplicationCiClass(application);
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "Application",
                application.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = application.CompanyId,
                    CiKey = $"application:{application.Id}",
                    Name = application.Name,
                    DisplayName = application.Name,
                    CiType = ciClass == "PlatformService" ? "Platform" : "Application",
                    CiClass = ciClass,
                    SourceEntityType = "Application",
                    SourceEntityId = application.Id,
                    Vendor = application.Vendor,
                    Manufacturer = application.Vendor,
                    Environment = application.Environment,
                    OperationalStatus = "Active",
                    LifecycleStatus = ResolveLifecycleStatus(application.Environment),
                    BusinessOwnerPersonId = ResolveBusinessOwnerPersonId(companyContext, department),
                    TechnicalOwnerPersonId = ResolveTechnicalOwnerPersonId(companyContext, department, null),
                    SupportTeamId = ResolveSupportTeamId(companyContext, department, null),
                    OwningDepartmentId = department?.Id,
                    OwningLobId = department?.BusinessUnitId,
                    ServiceTier = ResolveServiceTier(application.Criticality, ciClass == "PlatformService" ? "Platform" : "Application"),
                    ServiceClassification = ResolveApplicationServiceClassification(application),
                    BusinessCriticality = application.Criticality,
                    DataSensitivity = application.DataSensitivity,
                    MaintenanceWindow = ResolveMaintenanceWindow(application.Environment, ResolveTimeZone(companyContext, null), ciClass),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(10, 120)),
                    Notes = application.HostingModel
                });
        }

        foreach (var package in ResolveInstalledSoftwarePackages(world, companyContext.Company.Id))
        {
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "SoftwarePackage",
                package.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = companyContext.Company.Id,
                    CiKey = $"software:{package.Id}",
                    Name = package.Name,
                    DisplayName = package.Name,
                    CiType = "Software",
                    CiClass = "InstalledSoftware",
                    SourceEntityType = "SoftwarePackage",
                    SourceEntityId = package.Id,
                    Vendor = package.Vendor,
                    Manufacturer = package.Vendor,
                    Version = package.Version,
                    Environment = "Production",
                    OperationalStatus = "Active",
                    LifecycleStatus = "InService",
                    TechnicalOwnerPersonId = ResolvePlatformTechnicalOwner(companyContext),
                    SupportTeamId = ResolvePlatformSupportTeam(companyContext),
                    OwningDepartmentId = ResolvePlatformDepartmentId(companyContext),
                    OwningLobId = ResolvePlatformBusinessUnitId(companyContext),
                    ServiceTier = "Tier2",
                    ServiceClassification = "SoftwarePackage",
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(15, 180))
                });
        }

        foreach (var server in world.Servers.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "Server",
                server.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = server.CompanyId,
                    CiKey = $"server:{server.Id}",
                    Name = server.Hostname,
                    DisplayName = server.Hostname,
                    CiType = "Infrastructure",
                    CiClass = "Server",
                    SourceEntityType = "Server",
                    SourceEntityId = server.Id,
                    Manufacturer = "Microsoft",
                    Vendor = "Microsoft",
                    Model = server.ServerRole,
                    Version = server.OperatingSystemVersion,
                    Environment = server.Environment,
                    OperationalStatus = "Active",
                    LifecycleStatus = ResolveLifecycleStatus(server.Environment),
                    LocationType = "Office",
                    LocationId = server.OfficeId,
                    TechnicalOwnerPersonId = ResolveTechnicalOwnerPersonId(companyContext, null, server.OwnerTeamId),
                    SupportTeamId = !string.IsNullOrWhiteSpace(server.OwnerTeamId) ? server.OwnerTeamId : ResolvePlatformSupportTeam(companyContext),
                    OwningDepartmentId = ResolveTeamDepartmentId(companyContext, server.OwnerTeamId),
                    OwningLobId = companyContext.DepartmentsById.GetValueOrDefault(ResolveTeamDepartmentId(companyContext, server.OwnerTeamId) ?? string.Empty)?.BusinessUnitId,
                    ServiceTier = ResolveServiceTier(server.Criticality, "Server"),
                    ServiceClassification = "ServerInfrastructure",
                    BusinessCriticality = server.Criticality,
                    MaintenanceWindow = ResolveMaintenanceWindow(server.Environment, ResolveTimeZone(companyContext, server.OfficeId), "Infrastructure"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(7, 90)),
                    Notes = server.ServerRole
                });
        }

        foreach (var device in world.Devices.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "Device",
                device.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = device.CompanyId,
                    CiKey = $"device:{device.Id}",
                    Name = device.Hostname,
                    DisplayName = device.Hostname,
                    CiType = "Infrastructure",
                    CiClass = device.DeviceType,
                    SourceEntityType = "Device",
                    SourceEntityId = device.Id,
                    Manufacturer = device.Manufacturer,
                    Vendor = device.Manufacturer,
                    Model = device.Model,
                    Version = device.OperatingSystemVersion,
                    SerialNumber = device.SerialNumber,
                    AssetTag = device.AssetTag,
                    Environment = "Production",
                    OperationalStatus = "Active",
                    LifecycleStatus = "InService",
                    LocationType = "Office",
                    LocationId = device.AssignedOfficeId,
                    BusinessOwnerPersonId = device.AssignedPersonId,
                    TechnicalOwnerPersonId = ResolveTechnicalOwnerPersonId(companyContext, null, null),
                    SupportTeamId = ResolvePlatformSupportTeam(companyContext),
                    OwningDepartmentId = ResolvePersonDepartmentId(companyContext, device.AssignedPersonId),
                    OwningLobId = companyContext.DepartmentsById.GetValueOrDefault(ResolvePersonDepartmentId(companyContext, device.AssignedPersonId) ?? string.Empty)?.BusinessUnitId,
                    ServiceTier = "Tier3",
                    ServiceClassification = device.DeviceType.Contains("Privileged", StringComparison.OrdinalIgnoreCase)
                        ? "PrivilegedEndpoint"
                        : "EndUserEndpoint",
                    MaintenanceWindow = ResolveMaintenanceWindow("Production", ResolveTimeZone(companyContext, device.AssignedOfficeId), "Endpoint"),
                    LastReviewedAt = device.LastSeen,
                    Notes = device.OperatingSystem
                });
        }

        foreach (var asset in world.NetworkAssets.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "NetworkAsset",
                asset.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = asset.CompanyId,
                    CiKey = $"network:{asset.Id}",
                    Name = asset.Hostname,
                    DisplayName = asset.Hostname,
                    CiType = "Infrastructure",
                    CiClass = "NetworkDevice",
                    SourceEntityType = "NetworkAsset",
                    SourceEntityId = asset.Id,
                    Manufacturer = asset.Vendor,
                    Vendor = asset.Vendor,
                    Model = asset.Model,
                    Environment = "Production",
                    OperationalStatus = "Active",
                    LifecycleStatus = "InService",
                    LocationType = "Office",
                    LocationId = asset.OfficeId,
                    TechnicalOwnerPersonId = ResolvePlatformTechnicalOwner(companyContext),
                    SupportTeamId = ResolvePlatformSupportTeam(companyContext),
                    OwningDepartmentId = ResolvePlatformDepartmentId(companyContext),
                    OwningLobId = ResolvePlatformBusinessUnitId(companyContext),
                    ServiceTier = "Tier2",
                    ServiceClassification = "NetworkInfrastructure",
                    MaintenanceWindow = ResolveMaintenanceWindow("Production", ResolveTimeZone(companyContext, asset.OfficeId), "Infrastructure"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(14, 120))
                });
        }

        foreach (var asset in world.TelephonyAssets.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "TelephonyAsset",
                asset.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = asset.CompanyId,
                    CiKey = $"telephony:{asset.Id}",
                    Name = asset.Identifier,
                    DisplayName = asset.Identifier,
                    CiType = "Infrastructure",
                    CiClass = "TelephonyDevice",
                    SourceEntityType = "TelephonyAsset",
                    SourceEntityId = asset.Id,
                    Environment = "Production",
                    OperationalStatus = "Active",
                    LifecycleStatus = "InService",
                    LocationType = "Office",
                    LocationId = asset.AssignedOfficeId,
                    BusinessOwnerPersonId = asset.AssignedPersonId,
                    SupportTeamId = ResolvePlatformSupportTeam(companyContext),
                    OwningDepartmentId = ResolvePersonDepartmentId(companyContext, asset.AssignedPersonId),
                    OwningLobId = companyContext.DepartmentsById.GetValueOrDefault(ResolvePersonDepartmentId(companyContext, asset.AssignedPersonId) ?? string.Empty)?.BusinessUnitId,
                    ServiceTier = "Tier3",
                    ServiceClassification = "VoiceEndpoint",
                    MaintenanceWindow = ResolveMaintenanceWindow("Production", ResolveTimeZone(companyContext, asset.AssignedOfficeId), "Endpoint"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(30, 180))
                });
        }

        foreach (var database in world.Databases.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var department = companyContext.DepartmentsById.GetValueOrDefault(database.OwnerDepartmentId);
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "Database",
                database.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = database.CompanyId,
                    CiKey = $"database:{database.Id}",
                    Name = database.Name,
                    DisplayName = database.Name,
                    CiType = "Data",
                    CiClass = "Database",
                    SourceEntityType = "Database",
                    SourceEntityId = database.Id,
                    Manufacturer = database.Engine,
                    Vendor = database.Engine,
                    Model = database.Engine,
                    Environment = database.Environment,
                    OperationalStatus = "Active",
                    LifecycleStatus = ResolveLifecycleStatus(database.Environment),
                    BusinessOwnerPersonId = ResolveBusinessOwnerPersonId(companyContext, department),
                    TechnicalOwnerPersonId = ResolveTechnicalOwnerPersonId(companyContext, department, null),
                    SupportTeamId = ResolveSupportTeamId(companyContext, department, null),
                    OwningDepartmentId = department?.Id,
                    OwningLobId = department?.BusinessUnitId,
                    ServiceTier = ResolveServiceTier(ResolveDatabaseCriticality(database), "Data"),
                    ServiceClassification = "DataRepository",
                    BusinessCriticality = ResolveDatabaseCriticality(database),
                    DataSensitivity = database.Sensitivity,
                    MaintenanceWindow = ResolveMaintenanceWindow(database.Environment, ResolveTimeZone(companyContext, ResolveOfficeIdForServer(world, database.HostServerId)), "Data"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(10, 120))
                });
        }

        foreach (var share in world.FileShares.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var department = companyContext.DepartmentsById.GetValueOrDefault(share.OwnerDepartmentId);
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "FileShare",
                share.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = share.CompanyId,
                    CiKey = $"file-share:{share.Id}",
                    Name = share.ShareName,
                    DisplayName = share.ShareName,
                    CiType = "Data",
                    CiClass = "FileShare",
                    SourceEntityType = "FileShare",
                    SourceEntityId = share.Id,
                    Environment = "Production",
                    OperationalStatus = "Active",
                    LifecycleStatus = "InService",
                    BusinessOwnerPersonId = share.OwnerPersonId ?? ResolveBusinessOwnerPersonId(companyContext, department),
                    TechnicalOwnerPersonId = ResolveTechnicalOwnerPersonId(companyContext, department, null),
                    SupportTeamId = ResolveSupportTeamId(companyContext, department, null),
                    OwningDepartmentId = department?.Id,
                    OwningLobId = department?.BusinessUnitId,
                    ServiceTier = ResolveServiceTier(ResolveShareCriticality(share), "Data"),
                    ServiceClassification = "SharedDataRepository",
                    BusinessCriticality = ResolveShareCriticality(share),
                    DataSensitivity = share.Sensitivity,
                    MaintenanceWindow = ResolveMaintenanceWindow("Production", ResolveTimeZone(companyContext, ResolveOfficeIdForServer(world, share.HostServerId)), "Data"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(20, 180)),
                    Notes = share.UncPath
                });
        }

        foreach (var site in world.CollaborationSites.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var department = companyContext.DepartmentsById.GetValueOrDefault(site.OwnerDepartmentId);
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "CollaborationSite",
                site.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = site.CompanyId,
                    CiKey = $"collaboration:{site.Id}",
                    Name = site.Name,
                    DisplayName = site.Name,
                    CiType = "Collaboration",
                    CiClass = "CollaborationWorkspace",
                    SourceEntityType = "CollaborationSite",
                    SourceEntityId = site.Id,
                    Vendor = site.Platform,
                    Manufacturer = site.Platform,
                    Environment = "Production",
                    OperationalStatus = "Active",
                    LifecycleStatus = "InService",
                    BusinessOwnerPersonId = string.IsNullOrWhiteSpace(site.OwnerPersonId) ? ResolveBusinessOwnerPersonId(companyContext, department) : site.OwnerPersonId,
                    TechnicalOwnerPersonId = ResolveTechnicalOwnerPersonId(companyContext, department, null),
                    SupportTeamId = ResolveSupportTeamId(companyContext, department, null),
                    OwningDepartmentId = department?.Id,
                    OwningLobId = department?.BusinessUnitId,
                    ServiceTier = ResolveServiceTier(ResolveCollaborationCriticality(site), "Collaboration"),
                    ServiceClassification = site.Platform.Contains("Teams", StringComparison.OrdinalIgnoreCase)
                        ? "TeamWorkspace"
                        : "KnowledgeWorkspace",
                    BusinessCriticality = ResolveCollaborationCriticality(site),
                    DataSensitivity = site.PrivacyType == "Private" ? "Internal" : "Public",
                    MaintenanceWindow = ResolveMaintenanceWindow("Production", ResolveTimeZone(companyContext, null), "Collaboration"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(10, 150))
                });
        }
    }

    private void ProjectCanonicalRelationships(
        SyntheticEnterpriseWorld world,
        CompanyContext companyContext,
        IReadOnlyDictionary<string, ConfigurationItem> ciBySourceKey,
        CmdbProfile profile)
    {
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in world.ApplicationDependencies.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            if (!TryResolveCi(ciBySourceKey, "Application", dependency.SourceApplicationId, out var source)
                || !TryResolveCi(ciBySourceKey, "Application", dependency.TargetApplicationId, out var target))
            {
                continue;
            }

            AddRelationship(
                world,
                emitted,
                companyContext.Company.Id,
                source.Id,
                target.Id,
                "DependsOn",
                false,
                ResolveRelationshipConfidence(dependency.Criticality),
                dependency.InterfaceType,
                dependency.DependencyType);
        }

        foreach (var link in world.ApplicationRepositoryLinks.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            if (!TryResolveCi(ciBySourceKey, "Application", link.ApplicationId, out var source)
                || !TryResolveRepositoryCi(ciBySourceKey, link.RepositoryType, link.RepositoryId, out var target))
            {
                continue;
            }

            AddRelationship(
                world,
                emitted,
                companyContext.Company.Id,
                source.Id,
                target.Id,
                "StoresDataIn",
                string.Equals(link.RelationshipType, "PrimaryDataStore", StringComparison.OrdinalIgnoreCase),
                ResolveRelationshipConfidence(link.Criticality),
                "ApplicationRepositoryLink",
                link.RelationshipType);
        }

        foreach (var link in world.ApplicationTenantLinks.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            if (!TryResolveCi(ciBySourceKey, "Application", link.ApplicationId, out var source)
                || !TryResolveCi(ciBySourceKey, "CloudTenant", link.CloudTenantId, out var target))
            {
                continue;
            }

            AddRelationship(
                world,
                emitted,
                companyContext.Company.Id,
                source.Id,
                target.Id,
                "AssociatedWith",
                link.IsPrimary,
                "High",
                "ApplicationTenantLink",
                link.RelationshipType);
        }

        foreach (var hosting in world.ApplicationServiceHostings.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var service = world.ApplicationServices.FirstOrDefault(item => item.Id == hosting.ApplicationServiceId);
            if (service is null || !TryResolveCi(ciBySourceKey, "Application", service.ApplicationId, out var source))
            {
                continue;
            }

            ConfigurationItem? target = null;
            if (string.Equals(hosting.HostType, "Server", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(hosting.HostId))
            {
                TryResolveCi(ciBySourceKey, "Server", hosting.HostId!, out target);
            }
            else if (!string.IsNullOrWhiteSpace(hosting.HostId) && string.Equals(hosting.HostType, "CloudTenant", StringComparison.OrdinalIgnoreCase))
            {
                TryResolveCi(ciBySourceKey, "CloudTenant", hosting.HostId!, out target);
            }

            if (target is null)
            {
                continue;
            }

            AddRelationship(
                world,
                emitted,
                companyContext.Company.Id,
                source.Id,
                target.Id,
                "HostedOn",
                string.Equals(hosting.HostingRole, "Primary", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(hosting.HostingRole, "Application Server", StringComparison.OrdinalIgnoreCase),
                "High",
                hosting.DeploymentModel,
                hosting.HostingRole);
        }

        foreach (var database in world.Databases.Where(item => item.CompanyId == companyContext.Company.Id && !string.IsNullOrWhiteSpace(item.HostServerId)))
        {
            if (!TryResolveCi(ciBySourceKey, "Database", database.Id, out var source)
                || !TryResolveCi(ciBySourceKey, "Server", database.HostServerId!, out var target))
            {
                continue;
            }

            AddRelationship(world, emitted, companyContext.Company.Id, source.Id, target.Id, "HostedOn", true, "High", "DatabaseRepository", database.Engine);
        }

        foreach (var share in world.FileShares.Where(item => item.CompanyId == companyContext.Company.Id && !string.IsNullOrWhiteSpace(item.HostServerId)))
        {
            if (!TryResolveCi(ciBySourceKey, "FileShare", share.Id, out var source)
                || !TryResolveCi(ciBySourceKey, "Server", share.HostServerId!, out var target))
            {
                continue;
            }

            AddRelationship(world, emitted, companyContext.Company.Id, source.Id, target.Id, "HostedOn", true, "High", "FileShareRepository", share.AccessModel);
        }

        foreach (var installation in world.DeviceSoftwareInstallations)
        {
            var device = world.Devices.FirstOrDefault(item => item.Id == installation.DeviceId && item.CompanyId == companyContext.Company.Id);
            if (device is null)
            {
                continue;
            }

            if (!TryResolveCi(ciBySourceKey, "SoftwarePackage", installation.SoftwareId, out var source)
                || !TryResolveCi(ciBySourceKey, "Device", installation.DeviceId, out var target))
            {
                continue;
            }

            AddRelationship(world, emitted, companyContext.Company.Id, source.Id, target.Id, "InstalledOn", false, "High", "DeviceSoftwareInstallation", device.DeviceType);
        }

        foreach (var installation in world.ServerSoftwareInstallations)
        {
            var server = world.Servers.FirstOrDefault(item => item.Id == installation.ServerId && item.CompanyId == companyContext.Company.Id);
            if (server is null)
            {
                continue;
            }

            if (!TryResolveCi(ciBySourceKey, "SoftwarePackage", installation.SoftwareId, out var source)
                || !TryResolveCi(ciBySourceKey, "Server", installation.ServerId, out var target))
            {
                continue;
            }

            AddRelationship(world, emitted, companyContext.Company.Id, source.Id, target.Id, "InstalledOn", false, "High", "ServerSoftwareInstallation", server.ServerRole);
        }
    }

    private void GenerateSourceViews(SyntheticEnterpriseWorld world, CompanyContext companyContext, CmdbProfile profile, string deviationProfile)
    {
        var companyItems = world.ConfigurationItems.Where(item => item.CompanyId == companyContext.Company.Id).ToList();
        var companyRelationships = world.ConfigurationItemRelationships.Where(item => item.CompanyId == companyContext.Company.Id).ToList();
        var sourceRecordIdBySystemAndCiId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in companyItems)
        {
            CreateSourceRecordIfIncluded(world, companyContext, profile, deviationProfile, item, "CMDB", sourceRecordIdBySystemAndCiId);

            if (profile.IncludeAutoDiscoveryRecords)
            {
                CreateSourceRecordIfIncluded(world, companyContext, profile, deviationProfile, item, "AutoDiscovery", sourceRecordIdBySystemAndCiId);
            }

            if (profile.IncludeServiceCatalogRecords)
            {
                CreateSourceRecordIfIncluded(world, companyContext, profile, deviationProfile, item, "ServiceCatalog", sourceRecordIdBySystemAndCiId);
            }

            if (profile.IncludeSpreadsheetImportRecords)
            {
                CreateSourceRecordIfIncluded(world, companyContext, profile, deviationProfile, item, "SpreadsheetImport", sourceRecordIdBySystemAndCiId);
            }
        }

        foreach (var relationship in companyRelationships)
        {
            foreach (var sourceSystem in EnumerateSourceSystems(profile))
            {
                if (!sourceRecordIdBySystemAndCiId.TryGetValue(BuildSourceRecordKey(sourceSystem, relationship.SourceConfigurationItemId), out var sourceRecordId)
                    || !sourceRecordIdBySystemAndCiId.TryGetValue(BuildSourceRecordKey(sourceSystem, relationship.TargetConfigurationItemId), out var targetRecordId))
                {
                    continue;
                }

                if (!ShouldIncludeRelationshipInSource(relationship, sourceSystem, deviationProfile))
                {
                    continue;
                }

                world.CmdbSourceRelationships.Add(new CmdbSourceRelationship
                {
                    Id = _idFactory.Next("CMSR"),
                    CompanyId = relationship.CompanyId,
                    SourceSystem = sourceSystem,
                    SourceRelationshipId = $"{sourceSystem}-{relationship.Id}",
                    SourceRecordId = sourceRecordId,
                    TargetRecordId = targetRecordId,
                    RelationshipType = relationship.RelationshipType,
                    IsPrimary = relationship.IsPrimary,
                    Confidence = ResolveSourceConfidence(sourceSystem),
                    Status = "Active"
                });
            }
        }

        GenerateOrphanedSourceRecords(world, companyContext, profile, deviationProfile);
    }

    private void CreateSourceRecordIfIncluded(
        SyntheticEnterpriseWorld world,
        CompanyContext companyContext,
        CmdbProfile profile,
        string deviationProfile,
        ConfigurationItem item,
        string sourceSystem,
        IDictionary<string, string> sourceRecordIdBySystemAndCiId)
    {
        if (!ShouldIncludeInSource(item, sourceSystem, deviationProfile))
        {
            return;
        }

        var observed = BuildObservedRecord(item, companyContext, sourceSystem, deviationProfile);
        world.CmdbSourceRecords.Add(observed);
        world.CmdbSourceLinks.Add(new CmdbSourceLink
        {
            Id = _idFactory.Next("CMSL"),
            CompanyId = item.CompanyId,
            SourceRecordId = observed.Id,
            ConfigurationItemId = item.Id,
            LinkType = "Matched",
            MatchMethod = "SyntheticProjection",
            Confidence = observed.Confidence
        });
        sourceRecordIdBySystemAndCiId[BuildSourceRecordKey(sourceSystem, item.Id)] = observed.Id;
    }

    private void GenerateOrphanedSourceRecords(
        SyntheticEnterpriseWorld world,
        CompanyContext companyContext,
        CmdbProfile profile,
        string deviationProfile)
    {
        if (IsDeviationProfile(deviationProfile, ScenarioDeviationProfiles.Clean))
        {
            return;
        }

        if (profile.IncludeServiceCatalogRecords)
        {
            world.CmdbSourceRecords.Add(new CmdbSourceRecord
            {
                Id = _idFactory.Next("CMS"),
                CompanyId = companyContext.Company.Id,
                SourceSystem = "ServiceCatalog",
                SourceRecordId = $"CAT-{companyContext.Company.Id}-{_randomSource.Next(1000, 9999)}",
                CiType = "Application",
                CiClass = "BusinessApplication",
                Name = $"{companyContext.Company.Name} Innovation Lab POC",
                DisplayName = $"{companyContext.Company.Name} Innovation Lab POC",
                ObservedEnvironment = "Pilot",
                ObservedOperationalStatus = "Planned",
                ObservedLifecycleStatus = "ProofOfConcept",
                ObservedBusinessOwner = "Digital Innovation",
                ObservedServiceClassification = "Experimental",
                MatchStatus = "CatalogOnly",
                Confidence = "Low",
                LastSeen = _clock.UtcNow.AddDays(-_randomSource.Next(10, 45)),
                LastImported = _clock.UtcNow
            });
        }

        if (profile.IncludeSpreadsheetImportRecords)
        {
            world.CmdbSourceRecords.Add(new CmdbSourceRecord
            {
                Id = _idFactory.Next("CMS"),
                CompanyId = companyContext.Company.Id,
                SourceSystem = "SpreadsheetImport",
                SourceRecordId = $"XLS-{companyContext.Company.Id}-{_randomSource.Next(1000, 9999)}",
                CiType = "Infrastructure",
                CiClass = "Server",
                Name = $"{Slug(companyContext.Company.Name)}-legacy-archive-01",
                DisplayName = $"{companyContext.Company.Name} Legacy Archive Server",
                ObservedEnvironment = "Production",
                ObservedOperationalStatus = "Active",
                ObservedLifecycleStatus = "InService",
                ObservedTechnicalOwner = "Former Infrastructure Lead",
                ObservedServiceTier = "Tier1",
                MatchStatus = "Orphaned",
                Confidence = "Low",
                LastSeen = _clock.UtcNow.AddDays(-_randomSource.Next(120, 540)),
                LastImported = _clock.UtcNow
            });
        }
    }

    private CmdbSourceRecord BuildObservedRecord(
        ConfigurationItem item,
        CompanyContext companyContext,
        string sourceSystem,
        string deviationProfile)
    {
        var businessOwner = ResolvePersonDisplayName(companyContext, item.BusinessOwnerPersonId);
        var technicalOwner = ResolvePersonDisplayName(companyContext, item.TechnicalOwnerPersonId);
        var supportGroup = ResolveTeamName(companyContext, item.SupportTeamId);
        var location = ResolveLocationDisplayName(companyContext, item.LocationId);
        var observedCiClass = item.CiClass;
        var observedEnvironment = item.Environment;
        var observedOperationalStatus = item.OperationalStatus;
        var observedLifecycleStatus = item.LifecycleStatus;
        var observedServiceTier = item.ServiceTier;
        var observedServiceClassification = item.ServiceClassification;
        var observedCriticality = item.BusinessCriticality;
        var observedMaintenanceWindow = FormatMaintenanceWindow(item.MaintenanceWindow);

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "MissingBusinessOwner"))
        {
            businessOwner = null;
        }

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "MissingTechnicalOwner"))
        {
            technicalOwner = null;
        }

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "MissingSupportGroup"))
        {
            supportGroup = null;
        }

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "MissingCriticality"))
        {
            observedCriticality = null;
        }

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "MissingServiceTier"))
        {
            observedServiceTier = null;
        }

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "MissingServiceClassification"))
        {
            observedServiceClassification = null;
        }

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "MissingMaintenanceWindow"))
        {
            observedMaintenanceWindow = null;
        }

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "StaleOwner") && !string.IsNullOrWhiteSpace(businessOwner))
        {
            businessOwner = $"Former Employee - {businessOwner}";
        }

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "PlatformAsApplication")
            && string.Equals(item.CiType, "Platform", StringComparison.OrdinalIgnoreCase))
        {
            observedCiClass = "BusinessApplication";
        }

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "WrongEnvironment"))
        {
            observedEnvironment = string.Equals(item.Environment, "Production", StringComparison.OrdinalIgnoreCase)
                ? "Test"
                : "Production";
        }

        if (ShouldApplyDeviation(sourceSystem, deviationProfile, "WrongStatus"))
        {
            observedOperationalStatus = "Retired";
        }

        return new CmdbSourceRecord
        {
            Id = _idFactory.Next("CMS"),
            CompanyId = item.CompanyId,
            SourceSystem = sourceSystem,
            SourceRecordId = $"{sourceSystem}-{item.CiKey}",
            CiType = item.CiType,
            CiClass = observedCiClass,
            Name = item.Name,
            DisplayName = item.DisplayName,
            ObservedManufacturer = item.Manufacturer,
            ObservedVendor = item.Vendor,
            ObservedModel = item.Model,
            ObservedVersion = item.Version,
            ObservedSerialNumber = item.SerialNumber,
            ObservedAssetTag = item.AssetTag,
            ObservedLocation = location,
            ObservedEnvironment = observedEnvironment,
            ObservedOperationalStatus = observedOperationalStatus,
            ObservedLifecycleStatus = observedLifecycleStatus,
            ObservedBusinessOwner = businessOwner,
            ObservedTechnicalOwner = technicalOwner,
            ObservedSupportGroup = supportGroup,
            ObservedOwningLob = ResolveBusinessUnitName(companyContext, item.OwningLobId),
            ObservedServiceTier = observedServiceTier,
            ObservedServiceClassification = observedServiceClassification,
            ObservedBusinessCriticality = observedCriticality,
            ObservedMaintenanceWindow = observedMaintenanceWindow,
            MatchStatus = "Matched",
            Confidence = ResolveSourceConfidence(sourceSystem),
            LastSeen = ResolveLastSeen(item, sourceSystem),
            LastImported = _clock.UtcNow
        };
    }

    private bool ShouldIncludeInSource(ConfigurationItem item, string sourceSystem, string deviationProfile)
    {
        var baseProbability = sourceSystem switch
        {
            "CMDB" => item.CiType switch
            {
                "Application" or "Platform" or "BusinessService" => 0.82,
                "Data" => 0.74,
                "Infrastructure" => 0.68,
                "Software" => 0.52,
                _ => 0.65
            },
            "AutoDiscovery" => item.CiType switch
            {
                "Infrastructure" => 0.94,
                "Data" => 0.78,
                "Platform" => 0.55,
                "Application" => 0.34,
                "Software" => 0.85,
                _ => 0.20
            },
            "ServiceCatalog" => item.CiType switch
            {
                "Application" or "Platform" or "BusinessService" => 0.76,
                "Data" => 0.28,
                "Infrastructure" => 0.18,
                "Software" => 0.10,
                _ => 0.12
            },
            "SpreadsheetImport" => item.CiType switch
            {
                "Application" or "Platform" => 0.42,
                "Infrastructure" => 0.26,
                "Data" => 0.24,
                _ => 0.18
            },
            _ => 0.0
        };

        if (IsDeviationProfile(deviationProfile, ScenarioDeviationProfiles.Clean))
        {
            baseProbability = Math.Min(0.97, baseProbability + 0.15);
        }
        else if (IsDeviationProfile(deviationProfile, ScenarioDeviationProfiles.Aggressive))
        {
            baseProbability = Math.Max(0.08, baseProbability - 0.12);
        }

        return _randomSource.NextDouble() <= baseProbability;
    }

    private bool ShouldIncludeRelationshipInSource(ConfigurationItemRelationship relationship, string sourceSystem, string deviationProfile)
    {
        var baseProbability = sourceSystem switch
        {
            "CMDB" => relationship.RelationshipType switch
            {
                "SupportedBy" => 0.84,
                "DependsOn" => 0.58,
                "HostedOn" => 0.72,
                "InstalledOn" => 0.44,
                "StoresDataIn" => 0.71,
                "AssociatedWith" => 0.66,
                _ => 0.55
            },
            "AutoDiscovery" => relationship.RelationshipType switch
            {
                "HostedOn" or "InstalledOn" => 0.90,
                "StoresDataIn" => 0.64,
                "DependsOn" => 0.22,
                _ => 0.18
            },
            "ServiceCatalog" => relationship.RelationshipType switch
            {
                "SupportedBy" => 0.62,
                "DependsOn" => 0.28,
                "AssociatedWith" => 0.46,
                _ => 0.10
            },
            "SpreadsheetImport" => relationship.RelationshipType switch
            {
                "SupportedBy" => 0.20,
                "HostedOn" => 0.16,
                _ => 0.08
            },
            _ => 0.0
        };

        if (IsDeviationProfile(deviationProfile, ScenarioDeviationProfiles.Clean))
        {
            baseProbability = Math.Min(0.98, baseProbability + 0.18);
        }
        else if (IsDeviationProfile(deviationProfile, ScenarioDeviationProfiles.Aggressive))
        {
            baseProbability = Math.Max(0.02, baseProbability - 0.15);
        }

        return _randomSource.NextDouble() <= baseProbability;
    }

    private bool ShouldApplyDeviation(string sourceSystem, string deviationProfile, string deviationType)
    {
        if (IsDeviationProfile(deviationProfile, ScenarioDeviationProfiles.Clean))
        {
            return false;
        }

        var multiplier = IsDeviationProfile(deviationProfile, ScenarioDeviationProfiles.Aggressive) ? 1.8 : 1.0;
        var baseRate = deviationType switch
        {
            "MissingBusinessOwner" => sourceSystem == "CMDB" ? 0.18 : 0.10,
            "MissingTechnicalOwner" => sourceSystem == "CMDB" ? 0.12 : 0.08,
            "MissingSupportGroup" => sourceSystem == "CMDB" ? 0.10 : 0.06,
            "MissingCriticality" => sourceSystem == "CMDB" ? 0.26 : 0.14,
            "MissingServiceTier" => sourceSystem == "CMDB" ? 0.22 : 0.12,
            "MissingServiceClassification" => sourceSystem == "CMDB" ? 0.16 : 0.08,
            "MissingMaintenanceWindow" => sourceSystem == "CMDB" ? 0.30 : 0.12,
            "StaleOwner" => sourceSystem == "SpreadsheetImport" ? 0.28 : 0.10,
            "PlatformAsApplication" => sourceSystem == "ServiceCatalog" ? 0.16 : 0.05,
            "WrongEnvironment" => sourceSystem == "SpreadsheetImport" ? 0.20 : 0.06,
            "WrongStatus" => sourceSystem == "SpreadsheetImport" ? 0.18 : 0.03,
            _ => 0.0
        };

        return _randomSource.NextDouble() <= Math.Min(0.95, baseRate * multiplier);
    }

    private static bool IsDeviationProfile(string? deviationProfile, string name)
        => string.Equals(deviationProfile, name, StringComparison.OrdinalIgnoreCase)
           || (string.Equals(name, ScenarioDeviationProfiles.Clean, StringComparison.OrdinalIgnoreCase)
               && string.Equals(deviationProfile, "None", StringComparison.OrdinalIgnoreCase));

    private static string ResolveDeviationProfile(ScenarioDefinition scenario)
    {
        var configured = string.IsNullOrWhiteSpace(scenario.Cmdb.DeviationProfile)
            ? scenario.DeviationProfile
            : scenario.Cmdb.DeviationProfile;

        return ScenarioDeviationProfiles.All.Contains(configured ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? ScenarioDeviationProfiles.All.First(profile => profile.Equals(configured, StringComparison.OrdinalIgnoreCase))
            : ScenarioDeviationProfiles.Realistic;
    }

    private static bool TryResolveCi(
        IReadOnlyDictionary<string, ConfigurationItem> ciBySourceKey,
        string sourceType,
        string sourceId,
        out ConfigurationItem item)
        => ciBySourceKey.TryGetValue(BuildCiSourceKey(sourceType, sourceId), out item!);

    private static bool TryResolveRepositoryCi(
        IReadOnlyDictionary<string, ConfigurationItem> ciBySourceKey,
        string repositoryType,
        string repositoryId,
        out ConfigurationItem item)
    {
        var normalizedType = repositoryType switch
        {
            "Database" => "Database",
            "FileShare" => "FileShare",
            "CollaborationSite" => "CollaborationSite",
            _ => repositoryType
        };

        return TryResolveCi(ciBySourceKey, normalizedType, repositoryId, out item);
    }

    private void AddConfigurationItem(
        SyntheticEnterpriseWorld world,
        IDictionary<string, ConfigurationItem> ciBySourceKey,
        string sourceType,
        string sourceId,
        ConfigurationItem item)
    {
        var key = BuildCiSourceKey(sourceType, sourceId);
        if (ciBySourceKey.ContainsKey(key))
        {
            return;
        }

        world.ConfigurationItems.Add(item);
        ciBySourceKey[key] = item;
    }

    private void AddRelationship(
        SyntheticEnterpriseWorld world,
        ISet<string> emitted,
        string companyId,
        string sourceId,
        string targetId,
        string relationshipType,
        bool isPrimary,
        string confidence,
        string? sourceEvidence,
        string? notes)
    {
        var key = $"{sourceId}|{targetId}|{relationshipType}|{notes}";
        if (!emitted.Add(key))
        {
            return;
        }

        world.ConfigurationItemRelationships.Add(new ConfigurationItemRelationship
        {
            Id = _idFactory.Next("CIR"),
            CompanyId = companyId,
            SourceConfigurationItemId = sourceId,
            TargetConfigurationItemId = targetId,
            RelationshipType = relationshipType,
            IsPrimary = isPrimary,
            Confidence = confidence,
            SourceEvidence = sourceEvidence,
            Notes = notes
        });
    }

    private IReadOnlyList<SoftwarePackage> ResolveInstalledSoftwarePackages(SyntheticEnterpriseWorld world, string companyId)
    {
        var usedSoftwareIds = world.DeviceSoftwareInstallations.Select(item => item.SoftwareId)
            .Concat(world.ServerSoftwareInstallations.Select(item => item.SoftwareId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return world.SoftwarePackages
            .Where(item => usedSoftwareIds.Contains(item.Id)
                           && (world.DeviceSoftwareInstallations.Any(install => install.SoftwareId == item.Id && world.Devices.Any(device => device.Id == install.DeviceId && device.CompanyId == companyId))
                               || world.ServerSoftwareInstallations.Any(install => install.SoftwareId == item.Id && world.Servers.Any(server => server.Id == install.ServerId && server.CompanyId == companyId))))
            .ToList();
    }

    private string? ResolveBusinessOwnerPersonId(CompanyContext context, Department? department)
        => department is null
            ? ResolvePlatformBusinessOwner(context)
            : context.PeopleByDepartmentId.TryGetValue(department.Id, out var people)
                ? people.OrderByDescending(IsLikelyManager).ThenBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase).Select(person => person.Id).FirstOrDefault()
                : ResolvePlatformBusinessOwner(context);

    private string? ResolveTechnicalOwnerPersonId(CompanyContext context, Department? department, string? preferredTeamId)
    {
        if (!string.IsNullOrWhiteSpace(preferredTeamId)
            && context.TeamsById.TryGetValue(preferredTeamId, out var preferredTeam))
        {
            var teamMember = context.People.FirstOrDefault(person => string.Equals(person.TeamId, preferredTeam.Id, StringComparison.OrdinalIgnoreCase));
            if (teamMember is not null)
            {
                return teamMember.Id;
            }
        }

        if (department is not null
            && context.TeamsByDepartmentId.TryGetValue(department.Id, out var departmentTeams))
        {
            var teamMember = context.People.FirstOrDefault(person =>
                departmentTeams.Any(team => string.Equals(team.Id, person.TeamId, StringComparison.OrdinalIgnoreCase)));
            if (teamMember is not null)
            {
                return teamMember.Id;
            }
        }

        return ResolvePlatformTechnicalOwner(context);
    }

    private string? ResolveSupportTeamId(CompanyContext context, Department? department, string? preferredTeamId)
    {
        if (!string.IsNullOrWhiteSpace(preferredTeamId) && context.TeamsById.ContainsKey(preferredTeamId))
        {
            return preferredTeamId;
        }

        if (department is not null
            && context.TeamsByDepartmentId.TryGetValue(department.Id, out var departmentTeams)
            && departmentTeams.Count > 0)
        {
            return departmentTeams[0].Id;
        }

        return ResolvePlatformSupportTeam(context);
    }

    private string? ResolvePlatformBusinessOwner(CompanyContext context)
        => context.People
            .Where(person => IsTechnologyDepartment(context, person.DepartmentId))
            .OrderByDescending(IsLikelyManager)
            .ThenBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(person => person.Id)
            .FirstOrDefault()
           ?? context.People.OrderByDescending(IsLikelyManager).Select(person => person.Id).FirstOrDefault();

    private string? ResolvePlatformTechnicalOwner(CompanyContext context)
        => context.People
            .Where(person => IsTechnologyDepartment(context, person.DepartmentId))
            .OrderBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(person => person.Id)
            .FirstOrDefault()
           ?? context.People.Select(person => person.Id).FirstOrDefault();

    private string? ResolvePlatformSupportTeam(CompanyContext context)
        => context.Teams
            .FirstOrDefault(team => team.Name.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase)
                                    || team.Name.Contains("Operations", StringComparison.OrdinalIgnoreCase)
                                    || team.Name.Contains("Support", StringComparison.OrdinalIgnoreCase)
                                    || team.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase))?.Id
           ?? context.Teams.FirstOrDefault()?.Id;

    private string? ResolvePlatformDepartmentId(CompanyContext context)
        => context.Departments
            .FirstOrDefault(department => department.Name.Contains("Information Technology", StringComparison.OrdinalIgnoreCase)
                                          || department.Name.Contains("Security", StringComparison.OrdinalIgnoreCase)
                                          || department.Name.Contains("Engineering", StringComparison.OrdinalIgnoreCase))?.Id
           ?? context.Departments.FirstOrDefault()?.Id;

    private string? ResolvePlatformBusinessUnitId(CompanyContext context)
        => context.DepartmentsById.GetValueOrDefault(ResolvePlatformDepartmentId(context) ?? string.Empty)?.BusinessUnitId;

    private static bool IsTechnologyDepartment(CompanyContext context, string departmentId)
        => context.DepartmentsById.TryGetValue(departmentId, out var department)
           && (department.Name.Contains("Information Technology", StringComparison.OrdinalIgnoreCase)
               || department.Name.Contains("Engineering", StringComparison.OrdinalIgnoreCase)
               || department.Name.Contains("Security", StringComparison.OrdinalIgnoreCase)
               || department.Name.Contains("Operations", StringComparison.OrdinalIgnoreCase));

    private static int IsLikelyManager(Person person)
        => person.Title.Contains("Director", StringComparison.OrdinalIgnoreCase)
           || person.Title.Contains("Manager", StringComparison.OrdinalIgnoreCase)
           || person.Title.Contains("Lead", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

    private string ResolveApplicationCiClass(ApplicationRecord application)
    {
        if (application.BusinessCapability.Contains("Identity", StringComparison.OrdinalIgnoreCase)
            || application.Name.Contains("Entra", StringComparison.OrdinalIgnoreCase)
            || application.Name.Contains("Active Directory", StringComparison.OrdinalIgnoreCase)
            || application.Name.Contains("Databricks", StringComparison.OrdinalIgnoreCase)
            || application.Name.Contains("Intune", StringComparison.OrdinalIgnoreCase)
            || application.Name.Contains("Unity Catalog", StringComparison.OrdinalIgnoreCase))
        {
            return "PlatformService";
        }

        return application.HostingModel switch
        {
            "SaaS" => "SaaSApplication",
            "Hybrid" => "HybridApplication",
            "OnPremises" => "OnPremisesApplication",
            _ => "BusinessApplication"
        };
    }

    private static string ResolveApplicationServiceClassification(ApplicationRecord application)
        => application.HostingModel switch
        {
            "SaaS" => "ManagedBusinessApplication",
            "Hybrid" => "HybridBusinessApplication",
            _ => "BusinessApplication"
        };

    private static string ResolveLifecycleStatus(string environment)
        => environment switch
        {
            "Development" => "Pilot",
            "Staging" => "PreProduction",
            "Test" => "PreProduction",
            "UAT" => "PreProduction",
            _ => "InService"
        };

    private static string ResolveServiceTier(string? criticality, string ciType)
    {
        if (string.Equals(criticality, "High", StringComparison.OrdinalIgnoreCase))
        {
            return "Tier1";
        }

        return ciType switch
        {
            "BusinessService" or "Platform" => "Tier1",
            "Application" or "Data" => "Tier2",
            _ => "Tier3"
        };
    }

    private static string ResolveRelationshipConfidence(string? criticality)
        => string.Equals(criticality, "High", StringComparison.OrdinalIgnoreCase) ? "High" : "Medium";

    private static string ResolveDatabaseCriticality(DatabaseRepository database)
        => !string.IsNullOrWhiteSpace(database.AssociatedApplicationId) ? "High" : "Medium";

    private static string ResolveShareCriticality(FileShareRepository share)
        => share.Sensitivity is "Confidential" or "Restricted" ? "High" : "Medium";

    private static string ResolveCollaborationCriticality(CollaborationSite site)
        => string.Equals(site.WorkspaceType, "Executive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(site.PrivacyType, "Private", StringComparison.OrdinalIgnoreCase)
            ? "Medium"
            : "Low";

    private string ResolveTimeZone(CompanyContext context, string? officeId)
        => !string.IsNullOrWhiteSpace(officeId)
           && context.OfficesById.TryGetValue(officeId, out var office)
           && !string.IsNullOrWhiteSpace(office.TimeZone)
            ? office.TimeZone
            : context.Offices.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.TimeZone))?.TimeZone ?? "UTC";

    private string? ResolveOfficeIdForServer(SyntheticEnterpriseWorld world, string? serverId)
        => string.IsNullOrWhiteSpace(serverId)
            ? null
            : world.Servers.FirstOrDefault(server => string.Equals(server.Id, serverId, StringComparison.OrdinalIgnoreCase))?.OfficeId;

    private static MaintenanceWindowDefinition ResolveMaintenanceWindow(string environment, string timeZone, string ciType)
    {
        var start = environment switch
        {
            "Production" => ciType switch
            {
                "Infrastructure" => "22:00",
                "Data" => "21:00",
                "Platform" => "23:00",
                _ => "20:00"
            },
            _ => "19:00"
        };

        var day = environment switch
        {
            "Production" => "Saturday",
            "Staging" => "Thursday",
            "Development" => "Wednesday",
            _ => "Friday"
        };

        var duration = ciType switch
        {
            "Infrastructure" => 180,
            "Data" => 150,
            "Platform" => 120,
            _ => 90
        };

        return new MaintenanceWindowDefinition
        {
            DayOfWeek = day,
            StartTimeLocal = start,
            DurationMinutes = duration,
            TimeZone = timeZone,
            Frequency = "Weekly"
        };
    }

    private DateTimeOffset ResolveLastSeen(ConfigurationItem item, string sourceSystem)
    {
        var days = sourceSystem switch
        {
            "AutoDiscovery" => _randomSource.Next(0, 14),
            "CMDB" => _randomSource.Next(2, 60),
            "ServiceCatalog" => _randomSource.Next(14, 180),
            "SpreadsheetImport" => _randomSource.Next(30, 365),
            _ => 30
        };

        return _clock.UtcNow.AddDays(-days);
    }

    private static string ResolveSourceConfidence(string sourceSystem)
        => sourceSystem switch
        {
            "CMDB" => "Medium",
            "AutoDiscovery" => "High",
            "ServiceCatalog" => "Medium",
            "SpreadsheetImport" => "Low",
            _ => "Medium"
        };

    private static string? ResolvePersonDepartmentId(CompanyContext context, string? personId)
        => !string.IsNullOrWhiteSpace(personId) && context.PeopleById.TryGetValue(personId, out var person)
            ? person.DepartmentId
            : null;

    private static string? ResolveTeamDepartmentId(CompanyContext context, string? teamId)
        => !string.IsNullOrWhiteSpace(teamId) && context.TeamsById.TryGetValue(teamId, out var team)
            ? team.DepartmentId
            : null;

    private static string? ResolvePersonDisplayName(CompanyContext context, string? personId)
        => !string.IsNullOrWhiteSpace(personId) && context.PeopleById.TryGetValue(personId, out var person)
            ? person.DisplayName
            : null;

    private static string? ResolveTeamName(CompanyContext context, string? teamId)
        => !string.IsNullOrWhiteSpace(teamId) && context.TeamsById.TryGetValue(teamId, out var team)
            ? team.Name
            : null;

    private static string? ResolveBusinessUnitName(CompanyContext context, string? businessUnitId)
        => !string.IsNullOrWhiteSpace(businessUnitId) && context.BusinessUnitsById.TryGetValue(businessUnitId, out var businessUnit)
            ? businessUnit.Name
            : null;

    private static string? ResolveLocationDisplayName(CompanyContext context, string? officeId)
        => !string.IsNullOrWhiteSpace(officeId) && context.OfficesById.TryGetValue(officeId, out var office)
            ? office.Name
            : null;

    private static string? FormatMaintenanceWindow(MaintenanceWindowDefinition? window)
        => window is null
            ? null
            : $"{window.DayOfWeek} {window.StartTimeLocal} ({window.DurationMinutes}m {window.TimeZone})";

    private static string BuildCiSourceKey(string sourceType, string sourceId)
        => $"{sourceType}:{sourceId}";

    private static string BuildSourceRecordKey(string sourceSystem, string configurationItemId)
        => $"{sourceSystem}:{configurationItemId}";

    private static IReadOnlyList<string> EnumerateSourceSystems(CmdbProfile profile)
    {
        var systems = new List<string> { "CMDB" };
        if (profile.IncludeAutoDiscoveryRecords)
        {
            systems.Add("AutoDiscovery");
        }

        if (profile.IncludeServiceCatalogRecords)
        {
            systems.Add("ServiceCatalog");
        }

        if (profile.IncludeSpreadsheetImportRecords)
        {
            systems.Add("SpreadsheetImport");
        }

        return systems;
    }

    private static string Slug(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private sealed record CompanyContext(
        Company Company,
        IReadOnlyList<Department> Departments,
        IReadOnlyDictionary<string, Department> DepartmentsById,
        IReadOnlyList<Team> Teams,
        IReadOnlyDictionary<string, Team> TeamsById,
        IReadOnlyDictionary<string, List<Team>> TeamsByDepartmentId,
        IReadOnlyList<Person> People,
        IReadOnlyDictionary<string, Person> PeopleById,
        IReadOnlyDictionary<string, List<Person>> PeopleByDepartmentId,
        IReadOnlyList<Office> Offices,
        IReadOnlyDictionary<string, Office> OfficesById,
        IReadOnlyDictionary<string, BusinessUnit> BusinessUnitsById);
}
