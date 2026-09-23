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
            var observedAccountHolders = ProjectDirectoryAccountConfigurationItems(world, companyContext, ciBySourceKey);
            ProjectCanonicalRelationships(world, companyContext, ciBySourceKey, context.Scenario.Cmdb);
            GenerateSourceViews(world, companyContext, context.Scenario.Cmdb, deviationProfile, observedAccountHolders, ciBySourceKey);
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
        var applicationsById = world.Applications
            .Where(item => item.CompanyId == companyContext.Company.Id)
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var store in world.IdentityStores.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var criticality = ResolveIdentityStoreCriticality(store, companyContext.Company);
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
                    Fqdn = store.PrimaryDomain,
                    Environment = store.Environment,
                    OperationalStatus = "Active",
                    LifecycleStatus = "InService",
                    BusinessOwnerPersonId = ResolvePlatformBusinessOwner(companyContext),
                    TechnicalOwnerPersonId = ResolvePlatformTechnicalOwner(companyContext),
                    SupportTeamId = ResolvePlatformSupportTeam(companyContext),
                    OwningDepartmentId = ResolvePlatformDepartmentId(companyContext),
                    OwningLobId = ResolvePlatformBusinessUnitId(companyContext),
                    ServiceTier = ResolveServiceTier(criticality, "Platform"),
                    ServiceClassification = "IdentityPlatform",
                    BusinessCriticality = criticality,
                    MaintenanceWindow = ResolveMaintenanceWindow(store.Environment, ResolveTimeZone(companyContext, null), "Platform"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(5, 45))
                });
        }

        if (profile.IncludeCloudServices)
        {
            foreach (var tenant in ResolveCanonicalCloudTenants(world, companyContext.Company.Id))
            {
                var criticality = ResolveCloudTenantCriticality(tenant);
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
                        Fqdn = tenant.PrimaryDomain,
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
                        ServiceTier = ResolveServiceTier(criticality, "Platform"),
                        ServiceClassification = "CloudPlatform",
                        BusinessCriticality = criticality,
                        MaintenanceWindow = ResolveMaintenanceWindow(tenant.Environment, ResolveTimeZone(companyContext, null), "Platform"),
                        LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(7, 60))
                    });
            }
        }

        foreach (var application in world.Applications.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var department = companyContext.DepartmentsById.GetValueOrDefault(application.OwnerDepartmentId);
            var ciClass = ResolveApplicationCiClass(application);
            var criticality = ResolveApplicationCiCriticality(application, ciClass);
            var hasCurrentOwnershipEvidence = !string.IsNullOrWhiteSpace(application.OwnerDepartmentId);
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
                    BusinessOwnerPersonId = hasCurrentOwnershipEvidence
                        ? ResolveBusinessOwnerPersonId(companyContext, department)
                        : null,
                    TechnicalOwnerPersonId = hasCurrentOwnershipEvidence
                        ? ResolveTechnicalOwnerPersonId(companyContext, department, null)
                        : null,
                    SupportTeamId = hasCurrentOwnershipEvidence
                        ? ResolveSupportTeamId(companyContext, department, null)
                        : null,
                    OwningDepartmentId = hasCurrentOwnershipEvidence ? department?.Id : null,
                    OwningLobId = hasCurrentOwnershipEvidence ? department?.BusinessUnitId : null,
                    ServiceTier = ResolveServiceTier(criticality, ciClass == "PlatformService" ? "Platform" : "Application"),
                    ServiceClassification = ResolveApplicationServiceClassification(application),
                    BusinessCriticality = criticality,
                    DataSensitivity = application.DataSensitivity,
                    MaintenanceWindow = ResolveMaintenanceWindow(application.Environment, ResolveTimeZone(companyContext, null), ciClass),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(10, 120)),
                    Notes = application.HostingModel
                });
        }

        foreach (var package in ResolveInstalledSoftwarePackages(world, companyContext.Company.Id))
        {
            var criticality = ResolveSoftwarePackageCriticality(package);
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
                    ServiceTier = ResolveServiceTier(criticality, "Software"),
                    ServiceClassification = "SoftwarePackage",
                    BusinessCriticality = criticality,
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(15, 180))
                });
        }

        foreach (var server in world.Servers.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var criticality = ResolveServerCiCriticality(server);
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
                    Fqdn = BuildFqdn(server.Hostname, companyContext.Company.PrimaryDomain),
                    Environment = server.Environment,
                    OperationalStatus = "Active",
                    LifecycleStatus = ResolveLifecycleStatus(server.Environment),
                    LocationType = "Office",
                    LocationId = server.OfficeId,
                    TechnicalOwnerPersonId = ResolveTechnicalOwnerPersonId(companyContext, null, server.OwnerTeamId),
                    SupportTeamId = !string.IsNullOrWhiteSpace(server.OwnerTeamId) ? server.OwnerTeamId : ResolvePlatformSupportTeam(companyContext),
                    OwningDepartmentId = ResolveTeamDepartmentId(companyContext, server.OwnerTeamId),
                    OwningLobId = companyContext.DepartmentsById.GetValueOrDefault(ResolveTeamDepartmentId(companyContext, server.OwnerTeamId) ?? string.Empty)?.BusinessUnitId,
                    ServiceTier = ResolveServiceTier(criticality, "Server"),
                    ServiceClassification = "ServerInfrastructure",
                    BusinessCriticality = criticality,
                    MaintenanceWindow = ResolveMaintenanceWindow(server.Environment, ResolveTimeZone(companyContext, server.OfficeId), "Infrastructure"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(7, 90)),
                    Notes = server.ServerRole
                });
        }

        foreach (var device in world.Devices.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var criticality = ResolveDeviceCriticality(device);
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
                    Fqdn = BuildFqdn(device.Hostname, companyContext.Company.PrimaryDomain),
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
                    ServiceTier = ResolveServiceTier(criticality, "Endpoint"),
                    ServiceClassification = device.DeviceType.Contains("Privileged", StringComparison.OrdinalIgnoreCase)
                        ? "PrivilegedEndpoint"
                        : "EndUserEndpoint",
                    BusinessCriticality = criticality,
                    MaintenanceWindow = ResolveMaintenanceWindow("Production", ResolveTimeZone(companyContext, device.AssignedOfficeId), "Endpoint"),
                    LastReviewedAt = device.LastSeen,
                    Notes = device.OperatingSystem
                });
        }

        foreach (var asset in world.NetworkAssets.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var criticality = ResolveNetworkCriticality(asset);
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
                    Fqdn = BuildFqdn(asset.Hostname, companyContext.Company.PrimaryDomain),
                    Environment = "Production",
                    OperationalStatus = "Active",
                    LifecycleStatus = "InService",
                    LocationType = "Office",
                    LocationId = asset.OfficeId,
                    TechnicalOwnerPersonId = ResolvePlatformTechnicalOwner(companyContext),
                    SupportTeamId = ResolvePlatformSupportTeam(companyContext),
                    OwningDepartmentId = ResolvePlatformDepartmentId(companyContext),
                    OwningLobId = ResolvePlatformBusinessUnitId(companyContext),
                    ServiceTier = ResolveServiceTier(criticality, "Network"),
                    ServiceClassification = "NetworkInfrastructure",
                    BusinessCriticality = criticality,
                    MaintenanceWindow = ResolveMaintenanceWindow("Production", ResolveTimeZone(companyContext, asset.OfficeId), "Infrastructure"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(14, 120))
                });
        }

        foreach (var asset in world.TelephonyAssets.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var criticality = ResolveTelephonyCriticality(asset);
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
                    DisplayName = string.IsNullOrWhiteSpace(asset.DisplayName) ? asset.Identifier : asset.DisplayName,
                    CiType = "Infrastructure",
                    CiClass = "TelephonyDevice",
                    SourceEntityType = "TelephonyAsset",
                    SourceEntityId = asset.Id,
                    Manufacturer = asset.Vendor,
                    Vendor = asset.Vendor,
                    Model = asset.Model,
                    SerialNumber = asset.Extension,
                    Environment = "Production",
                    OperationalStatus = "Active",
                    LifecycleStatus = "InService",
                    LocationType = "Office",
                    LocationId = asset.AssignedOfficeId,
                    BusinessOwnerPersonId = asset.AssignedPersonId,
                    SupportTeamId = ResolvePlatformSupportTeam(companyContext),
                    OwningDepartmentId = ResolvePersonDepartmentId(companyContext, asset.AssignedPersonId),
                    OwningLobId = companyContext.DepartmentsById.GetValueOrDefault(ResolvePersonDepartmentId(companyContext, asset.AssignedPersonId) ?? string.Empty)?.BusinessUnitId,
                    ServiceTier = ResolveServiceTier(criticality, "Endpoint"),
                    ServiceClassification = "VoiceEndpoint",
                    BusinessCriticality = criticality,
                    MaintenanceWindow = ResolveMaintenanceWindow("Production", ResolveTimeZone(companyContext, asset.AssignedOfficeId), "Endpoint"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(30, 180)),
                    Notes = string.IsNullOrWhiteSpace(asset.PhoneNumber)
                        ? asset.Extension
                        : $"{asset.PhoneNumber}{(string.IsNullOrWhiteSpace(asset.Extension) ? string.Empty : $" ext. {asset.Extension}")}"
                });
        }

        foreach (var database in world.Databases.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var department = companyContext.DepartmentsById.GetValueOrDefault(database.OwnerDepartmentId);
            var criticality = ResolveDatabaseCriticality(database, applicationsById);
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
                    ServiceTier = ResolveServiceTier(criticality, "Data"),
                    ServiceClassification = "DataRepository",
                    BusinessCriticality = criticality,
                    DataSensitivity = database.Sensitivity,
                    MaintenanceWindow = ResolveMaintenanceWindow(database.Environment, ResolveTimeZone(companyContext, ResolveOfficeIdForServer(world, database.HostServerId)), "Data"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(10, 120))
                });
        }

        foreach (var share in world.FileShares.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var department = companyContext.DepartmentsById.GetValueOrDefault(share.OwnerDepartmentId);
            var criticality = ResolveShareCriticality(share);
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
                    ServiceTier = ResolveServiceTier(criticality, "Data"),
                    ServiceClassification = "SharedDataRepository",
                    BusinessCriticality = criticality,
                    DataSensitivity = share.Sensitivity,
                    MaintenanceWindow = ResolveMaintenanceWindow("Production", ResolveTimeZone(companyContext, ResolveOfficeIdForServer(world, share.HostServerId)), "Data"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(20, 180)),
                    UncPath = share.UncPath,
                    Notes = share.UncPath
                });
        }

        foreach (var site in world.CollaborationSites.Where(item => item.CompanyId == companyContext.Company.Id))
        {
            var department = companyContext.DepartmentsById.GetValueOrDefault(site.OwnerDepartmentId);
            var criticality = ResolveCollaborationCriticality(site);
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
                    ServiceTier = ResolveServiceTier(criticality, "Collaboration"),
                    ServiceClassification = site.Platform.Contains("Teams", StringComparison.OrdinalIgnoreCase)
                        ? "TeamWorkspace"
                        : "KnowledgeWorkspace",
                    BusinessCriticality = criticality,
                    DataSensitivity = site.PrivacyType == "Private" ? "Internal" : "Public",
                    MaintenanceWindow = ResolveMaintenanceWindow("Production", ResolveTimeZone(companyContext, null), "Collaboration"),
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(10, 150))
                });
        }
    }

    /// <summary>
    /// Projects one configuration item per directory account the company holds. The item describes
    /// the account object; it never asserts anything the object does not already say. The owner
    /// columns carry the person identifier the account itself is linked to, verbatim, so a consumer
    /// resolves it against its own person records; an account with no link leaves them empty rather
    /// than borrowing an owner from its department, its team or the platform.
    /// </summary>
    /// <returns>
    /// The owner text each account object carries in its own name attributes, keyed by the
    /// identifier of the configuration item that describes it, for the source views to observe.
    /// </returns>
    private IReadOnlyDictionary<string, string> ProjectDirectoryAccountConfigurationItems(
        SyntheticEnterpriseWorld world,
        CompanyContext companyContext,
        IDictionary<string, ConfigurationItem> ciBySourceKey)
    {
        var observedHolderByCiId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var companyAccounts = world.Accounts
            .Where(account => string.Equals(account.CompanyId, companyContext.Company.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (companyAccounts.Count == 0)
        {
            return observedHolderByCiId;
        }

        // An invited account names its sponsoring account, so the sponsor is resolved through the
        // accounts the company holds rather than through any identifier minted here.
        var accountsById = companyAccounts
            .GroupBy(account => account.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var account in companyAccounts)
        {
            var criticality = ResolveDirectoryAccountCriticality(account);
            var owningDepartmentId = ResolvePersonDepartmentId(companyContext, account.PersonId);

            // The account identifier is unique across the world, so it is unique within the company:
            // AddConfigurationItem keys on it and leaves an already described account alone.
            AddConfigurationItem(
                world,
                ciBySourceKey,
                "DirectoryAccount",
                account.Id,
                new ConfigurationItem
                {
                    Id = _idFactory.Next("CI"),
                    CompanyId = account.CompanyId,
                    CiKey = $"directory-account:{account.Id}",
                    Name = account.SamAccountName,
                    DisplayName = account.UserPrincipalName,
                    CiType = "Identity",
                    CiClass = ResolveDirectoryAccountCiClass(account),
                    SourceEntityType = "DirectoryAccount",
                    SourceEntityId = account.Id,
                    Environment = "Production",

                    // The enabled state of the object is the whole of its status. A retained
                    // disabled object is still in the directory, so it is described as disabled
                    // rather than removed.
                    OperationalStatus = account.Enabled ? "Active" : "Disabled",
                    LifecycleStatus = account.Enabled ? "InService" : "Retired",

                    // The raw person identifier the directory object carries, unchanged.
                    BusinessOwnerPersonId = account.PersonId,
                    TechnicalOwnerPersonId = ResolveDirectoryAccountSponsorPersonId(accountsById, account),

                    // A directory holds no support group for an account object, so none is claimed.
                    SupportTeamId = null,
                    OwningDepartmentId = owningDepartmentId,
                    OwningLobId = companyContext.DepartmentsById.GetValueOrDefault(owningDepartmentId ?? string.Empty)?.BusinessUnitId,
                    ServiceTier = ResolveDirectoryAccountServiceTier(criticality),
                    ServiceClassification = ResolveDirectoryAccountServiceClassification(account),
                    BusinessCriticality = criticality,
                    InstallDate = account.WhenCreated,
                    LastReviewedAt = _clock.UtcNow.AddDays(-_randomSource.Next(10, 240)),

                    // The description attribute the object carries, copied as it stands. Where that
                    // prose is the only record of who owns the object, it stays prose: nothing is
                    // read out of it into an owner column.
                    Notes = account.Description
                });

            if (!ciBySourceKey.TryGetValue(BuildCiSourceKey("DirectoryAccount", account.Id), out var item))
            {
                continue;
            }

            var observedHolder = ResolveObservedAccountHolder(account);
            if (!string.IsNullOrWhiteSpace(observedHolder))
            {
                observedHolderByCiId[item.Id] = observedHolder;
            }
        }

        return observedHolderByCiId;
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

    private void GenerateSourceViews(
        SyntheticEnterpriseWorld world,
        CompanyContext companyContext,
        CmdbProfile profile,
        string deviationProfile,
        IReadOnlyDictionary<string, string> observedHolderByCiId,
        IDictionary<string, ConfigurationItem> ciBySourceKey)
    {
        var companyItems = world.ConfigurationItems.Where(item => item.CompanyId == companyContext.Company.Id).ToList();
        var companyRelationships = world.ConfigurationItemRelationships.Where(item => item.CompanyId == companyContext.Company.Id).ToList();
        var recordBySystemAndCiId = new Dictionary<string, CmdbSourceRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in companyItems)
        {
            CreateSourceRecordIfIncluded(world, companyContext, profile, deviationProfile, item, "CMDB", recordBySystemAndCiId, observedHolderByCiId, ciBySourceKey);

            if (profile.IncludeAutoDiscoveryRecords)
            {
                CreateSourceRecordIfIncluded(world, companyContext, profile, deviationProfile, item, "AutoDiscovery", recordBySystemAndCiId, observedHolderByCiId, ciBySourceKey);
            }

            if (profile.IncludeServiceCatalogRecords)
            {
                CreateSourceRecordIfIncluded(world, companyContext, profile, deviationProfile, item, "ServiceCatalog", recordBySystemAndCiId, observedHolderByCiId, ciBySourceKey);
            }

            if (profile.IncludeSpreadsheetImportRecords)
            {
                CreateSourceRecordIfIncluded(world, companyContext, profile, deviationProfile, item, "SpreadsheetImport", recordBySystemAndCiId, observedHolderByCiId, ciBySourceKey);
            }
        }

        foreach (var relationship in companyRelationships)
        {
            foreach (var sourceSystem in EnumerateSourceSystems(profile))
            {
                if (!recordBySystemAndCiId.TryGetValue(BuildSourceRecordKey(sourceSystem, relationship.SourceConfigurationItemId), out var sourceRecord)
                    || !recordBySystemAndCiId.TryGetValue(BuildSourceRecordKey(sourceSystem, relationship.TargetConfigurationItemId), out var targetRecord))
                {
                    continue;
                }

                var sourceRecordId = sourceRecord.Id;
                var targetRecordId = targetRecord.Id;

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
        IDictionary<string, CmdbSourceRecord> recordBySystemAndCiId,
        IReadOnlyDictionary<string, string> observedHolderByCiId,
        IDictionary<string, ConfigurationItem> ciBySourceKey)
    {
        if (!ShouldIncludeInSource(item, sourceSystem, deviationProfile))
        {
            return;
        }

        var observed = BuildObservedRecord(item, companyContext, sourceSystem, deviationProfile, observedHolderByCiId);
        var unreconciled = TryAddUnreconciledConfigurationItem(world, ciBySourceKey, recordBySystemAndCiId, item, observed, sourceSystem);
        if (unreconciled is not null)
        {
            observed = observed with { MatchStatus = "Unreconciled" };
        }

        world.CmdbSourceRecords.Add(observed);
        world.CmdbSourceLinks.Add(new CmdbSourceLink
        {
            Id = _idFactory.Next("CMSL"),
            CompanyId = item.CompanyId,
            SourceRecordId = observed.Id,
            ConfigurationItemId = (unreconciled ?? item).Id,
            LinkType = unreconciled is null ? "Matched" : "Duplicate",
            MatchMethod = "SyntheticProjection",
            Confidence = observed.Confidence
        });
        recordBySystemAndCiId[BuildSourceRecordKey(sourceSystem, item.Id)] = observed;
    }

    /// <summary>
    /// A configuration management database matches an incoming record to the item it already holds
    /// by the identity the record states. When a source states a different identity for the same
    /// thing - the fully qualified name of a host the database holds by its short name, the vendor
    /// qualified product name of an application the database holds by its own - the match fails and
    /// the database ends up holding a second item for that thing, carrying the values that source
    /// reported. Nothing about the underlying entity changes, so both items still name it through
    /// <see cref="ConfigurationItem.SourceEntityType"/> and <see cref="ConfigurationItem.SourceEntityId"/>,
    /// and a rollup over either item's attributes has two disagreeing rows to resolve.
    /// </summary>
    /// <remarks>
    /// Three conditions hold the shape to what can be stated honestly. The database must already
    /// hold the thing through its own CMDB record, so the disagreement is always traceable to two
    /// distinct source records rather than to one record and an unrecorded assumption; that record
    /// must state a criticality, so the value the duplicate disagrees with was actually reported;
    /// and the incoming record must state one too, because every configuration item this generator
    /// emits states a criticality and inventing one for a record that reported none would be
    /// filling a column rather than reporting a fact.
    ///
    /// Matching is by the identity a record states, not by which system filed it, so a second
    /// source stating the same identity matches the item the first one left behind and files
    /// another record against it. A database does not accumulate one item per importer.
    /// </remarks>
    private ConfigurationItem? TryAddUnreconciledConfigurationItem(
        SyntheticEnterpriseWorld world,
        IDictionary<string, ConfigurationItem> ciBySourceKey,
        IDictionary<string, CmdbSourceRecord> recordBySystemAndCiId,
        ConfigurationItem item,
        CmdbSourceRecord observed,
        string sourceSystem)
    {
        if (string.Equals(sourceSystem, "CMDB", StringComparison.OrdinalIgnoreCase))
        {
            // The CMDB record is the database's own row for the item. It cannot fail to match it.
            return null;
        }

        if (string.Equals(observed.Name, item.Name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(item.SourceEntityType) || string.IsNullOrWhiteSpace(item.SourceEntityId))
        {
            return null;
        }

        // The identity the record states is what the database matches on, so a record that states
        // an identity the database already holds an item under matches that item.
        var recordedIdentityOrigin = $"Recorded:{Slug(observed.Name)}";
        var recordedIdentityKey = BuildCiSourceKey(item.SourceEntityType!, item.SourceEntityId!, recordedIdentityOrigin);
        if (ciBySourceKey.TryGetValue(recordedIdentityKey, out var alreadyHeld))
        {
            return alreadyHeld;
        }

        if (string.IsNullOrWhiteSpace(observed.ObservedBusinessCriticality))
        {
            return null;
        }

        if (!recordBySystemAndCiId.TryGetValue(BuildSourceRecordKey("CMDB", item.Id), out var canonicalRecord)
            || string.IsNullOrWhiteSpace(canonicalRecord.ObservedBusinessCriticality))
        {
            return null;
        }

        var duplicate = new ConfigurationItem
        {
            Id = _idFactory.Next("CI"),
            CompanyId = item.CompanyId,
            CiKey = $"{item.CiKey}#{Slug(observed.Name)}",
            Name = observed.Name,
            DisplayName = observed.DisplayName,
            CiType = observed.CiType,
            CiClass = observed.CiClass,
            SourceEntityType = item.SourceEntityType,
            SourceEntityId = item.SourceEntityId,

            // Only what the record reported. The owner columns hold person and team identifiers,
            // and an unmatched record carries owner text that was never resolved to either, so they
            // stay empty rather than borrowing the identifiers the matched item holds.
            Manufacturer = observed.ObservedManufacturer,
            Vendor = observed.ObservedVendor,
            Model = observed.ObservedModel,
            Version = observed.ObservedVersion,
            SerialNumber = observed.ObservedSerialNumber,
            AssetTag = observed.ObservedAssetTag,
            BusinessCriticality = observed.ObservedBusinessCriticality,
            Notes = null
        };

        // Columns the record did not fill keep the value a database records when nothing filled
        // them, rather than the value the matched item holds: the two items never met, so nothing
        // could have carried a value across.
        if (!string.IsNullOrWhiteSpace(observed.ObservedEnvironment))
        {
            duplicate = duplicate with { Environment = observed.ObservedEnvironment! };
        }

        if (!string.IsNullOrWhiteSpace(observed.ObservedOperationalStatus))
        {
            duplicate = duplicate with { OperationalStatus = observed.ObservedOperationalStatus! };
        }

        if (!string.IsNullOrWhiteSpace(observed.ObservedLifecycleStatus))
        {
            duplicate = duplicate with { LifecycleStatus = observed.ObservedLifecycleStatus! };
        }

        if (!string.IsNullOrWhiteSpace(observed.ObservedServiceTier))
        {
            duplicate = duplicate with { ServiceTier = observed.ObservedServiceTier! };
        }

        if (!string.IsNullOrWhiteSpace(observed.ObservedServiceClassification))
        {
            duplicate = duplicate with { ServiceClassification = observed.ObservedServiceClassification! };
        }

        return AddConfigurationItem(
            world,
            ciBySourceKey,
            item.SourceEntityType!,
            item.SourceEntityId!,
            duplicate,
            origin: recordedIdentityOrigin);
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
            SourceRecordId = $"CAT-APP-{StableRecordNumber(companyContext.Company.Id, 6)}",
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
            SourceRecordId = $"XLS-SRV-{StableRecordNumber($"{companyContext.Company.Id}-legacy-archive", 6)}",
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
        string deviationProfile,
        IReadOnlyDictionary<string, string> observedHolderByCiId)
    {
        // A collector reading a directory account fills its owner column in from the name
        // attributes the object carries, which are not always the name of the person the object is
        // linked to. Every other class has its canonical owner read back by name.
        var businessOwner = observedHolderByCiId.TryGetValue(item.Id, out var observedHolder)
            ? observedHolder
            : ResolvePersonDisplayName(companyContext, item.BusinessOwnerPersonId);
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
        var observedName = item.Name;
        var observedDisplayName = item.DisplayName;

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

        // Criticality is a judgement, not a measurement, and the systems outside the configuration
        // database make it from what they can see. A sheet or a catalog entry a service owner keeps
        // states the band that owner argued for; a discovery scan has no business context at all
        // and files what its defaults say. Either way the band a source states is its own, and need
        // not be the one the database holds.
        if (!string.IsNullOrWhiteSpace(observedCriticality)
            && ShouldApplyDeviation(sourceSystem, deviationProfile, "MisjudgedCriticality"))
        {
            observedCriticality = ShiftBusinessCriticality(observedCriticality!, sourceSystem);
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

        // Systems outside the configuration database call a thing by the name their own view of it
        // supplies: a scan resolves a host to its fully qualified name, a catalog lists a product
        // under its vendor's name for it. Both name the same thing; neither is the spelling the
        // database holds.
        if (TryResolveAlternateRecordedIdentity(item, out var alternateIdentity)
            && ShouldApplyDeviation(sourceSystem, deviationProfile, "DivergentRecordedIdentity"))
        {
            observedName = alternateIdentity;
            observedDisplayName = alternateIdentity;
        }

        return new CmdbSourceRecord
        {
            Id = _idFactory.Next("CMS"),
            CompanyId = item.CompanyId,
            SourceSystem = sourceSystem,
            SourceRecordId = BuildObservedSourceRecordId(sourceSystem, item),
            CiType = item.CiType,
            CiClass = observedCiClass,
            Name = observedName,
            DisplayName = observedDisplayName,
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

    private static string BuildObservedSourceRecordId(string sourceSystem, ConfigurationItem item)
    {
        var classCode = ResolveSourceClassCode(item);
        var recordNumber = StableRecordNumber(item.CiKey, 7);

        return sourceSystem switch
        {
            "CMDB" => $"CI{recordNumber}",
            "AutoDiscovery" => $"DISC-{classCode}-{recordNumber}",
            "SpreadsheetImport" => $"XLS-{classCode}-{recordNumber}",
            "ServiceCatalog" => $"CAT-{classCode}-{recordNumber}",
            _ => $"{ResolveSourceSystemCode(sourceSystem)}-{classCode}-{recordNumber}"
        };
    }

    private static string ResolveSourceClassCode(ConfigurationItem item)
        => item.CiClass switch
        {
            "Workstation" => "WS",
            "PrivilegedAccessWorkstation" => "PAW",
            "Server" => "SRV",
            "NetworkDevice" => "NET",
            "TelephonyDevice" => "TEL",
            "FileShare" => "FS",
            "CollaborationWorkspace" => "CWS",
            "Database" => "DB",
            "DirectoryService" => "DIR",
            "SaaSApplication" => "SAS",
            "HybridApplication" => "HYB",
            "InstalledSoftware" => "SW",
            "UserAccount" => "USR",
            "SecondaryAccount" => "SEC",
            "PrivilegedAccount" => "ADM",
            "ServiceAccount" => "SVC",
            "SharedMailboxAccount" => "SMB",
            "BuiltInAccount" => "BIA",
            "MachineAccount" => "MAC",
            "GuestAccount" => "GST",
            "ContractorAccount" => "CTR",
            "ManagedServiceProviderAccount" => "MSP",
            _ => new string(item.CiClass.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant() is { Length: > 0 } code ? code : "CI"
        };

    private static string ResolveSourceSystemCode(string sourceSystem)
        => sourceSystem switch
        {
            "CMDB" => "CI",
            "AutoDiscovery" => "DISC",
            "SpreadsheetImport" => "XLS",
            "ServiceCatalog" => "CAT",
            _ => new string(sourceSystem.Where(char.IsLetterOrDigit).Take(4).ToArray()).ToUpperInvariant() is { Length: > 0 } code ? code : "SRC"
        };

    private static string StableRecordNumber(string value, int digits)
    {
        var max = 1;
        for (var i = 0; i < digits; i++)
        {
            max *= 10;
        }

        var number = (int)(ComputeStableHash(value) % (uint)max);
        if (number < 0)
        {
            number = -number;
        }

        return number.ToString($"D{digits}");
    }

    private static uint ComputeStableHash(string value)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;

            foreach (var character in value)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash;
        }
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

                // A configuration database holds the account objects somebody chose to record,
                // which is rarely all of them.
                "Identity" => 0.55,
                _ => 0.65
            },
            "AutoDiscovery" => item.CiType switch
            {
                "Infrastructure" => 0.94,
                "Data" => 0.78,
                "Platform" => 0.55,
                "Application" => 0.34,
                "Software" => 0.85,

                // A directory read returns nearly every account object it can see.
                "Identity" => 0.92,
                _ => 0.20
            },
            "ServiceCatalog" => item.CiType switch
            {
                "Application" or "Platform" or "BusinessService" => 0.76,
                "Data" => 0.28,
                "Infrastructure" => 0.18,
                "Software" => 0.10,
                "Identity" => 0.06,
                _ => 0.12
            },
            "SpreadsheetImport" => item.CiType switch
            {
                "Application" or "Platform" => 0.42,
                "Infrastructure" => 0.26,
                "Data" => 0.24,
                "Identity" => 0.14,
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

            // The configuration database's own record states the band the database holds, so it
            // cannot disagree with itself; every other source states its own judgement.
            "MisjudgedCriticality" => sourceSystem switch
            {
                "SpreadsheetImport" => 0.38,
                "ServiceCatalog" => 0.30,
                "AutoDiscovery" => 0.16,
                _ => 0.0
            },

            // Likewise for the identity a source states: the database's own record spells the name
            // the database holds.
            "DivergentRecordedIdentity" => sourceSystem switch
            {
                "SpreadsheetImport" => 0.24,
                "ServiceCatalog" => 0.18,
                "AutoDiscovery" => 0.12,
                _ => 0.0
            },
            _ => 0.0
        };

        return _randomSource.NextDouble() <= Math.Min(0.95, baseRate * multiplier);
    }

    /// <summary>
    /// The second name the item itself already holds for the same thing, if it holds one: the fully
    /// qualified name of a host recorded by its short name, or the vendor-qualified product name of
    /// an application recorded under its own. Nothing is composed that the item does not already
    /// state, so where an item holds no second name for itself no source can state one.
    /// </summary>
    /// <remarks>
    /// Identity objects are left out on purpose. A directory read returns an immutable object
    /// identifier alongside the name, so a directory account matches on that identifier however it
    /// is spelled, and a second account item could never arise this way.
    /// </remarks>
    private static bool TryResolveAlternateRecordedIdentity(ConfigurationItem item, out string identity)
    {
        if (string.Equals(item.CiType, "Infrastructure", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(item.Fqdn)
            && !string.Equals(item.Fqdn, item.Name, StringComparison.OrdinalIgnoreCase))
        {
            identity = item.Fqdn!;
            return true;
        }

        if (string.Equals(item.CiType, "Application", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(item.Vendor)
            && !item.Name.StartsWith(item.Vendor!, StringComparison.OrdinalIgnoreCase))
        {
            identity = $"{item.Vendor} {item.Name}";
            return true;
        }

        identity = string.Empty;
        return false;
    }

    /// <summary>
    /// Moves a criticality band the way the source that states it leans. A sheet or a catalog entry
    /// a service owner keeps argues its own service upward, sometimes straight to the top band; a
    /// discovery scan, which knows nothing about the business, files toward the bottom. A band
    /// already at the end a source leans towards is the band that source states, so it is returned
    /// unchanged rather than turned around into a move the source had no reason to make.
    /// </summary>
    private string ShiftBusinessCriticality(string criticality, string sourceSystem)
    {
        var ladder = BusinessCriticalityLadder;
        var index = Array.FindIndex(ladder, value => string.Equals(value, criticality, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return criticality;
        }

        var upward = !string.Equals(sourceSystem, "AutoDiscovery", StringComparison.OrdinalIgnoreCase);
        var roll = _randomSource.NextDouble();
        var distance = roll <= 0.55 ? 1 : roll <= 0.85 ? 2 : ladder.Length - 1;

        return ladder[Math.Clamp(upward ? index + distance : index - distance, 0, ladder.Length - 1)];
    }

    private static readonly string[] BusinessCriticalityLadder = ["Low", "Medium", "High", "MissionCritical"];

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

    /// <summary>
    /// Records one configuration item for a source entity and returns it, or returns null when that
    /// entity has already been described from the same origin.
    /// </summary>
    /// <remarks>
    /// The projection passes over an entity from several directions - a server is reached as a
    /// server and again as the host of something installed on it - so without a guard the same
    /// entity would be described twice by accident, and every count taken over the table would be
    /// wrong. <paramref name="origin"/> names the identity the description was filed under, so that
    /// guard still collapses every repeated canonical projection of one entity, while a description
    /// a source genuinely filed under an identity the database does not already hold can stand as
    /// its own item.
    /// </remarks>
    private ConfigurationItem? AddConfigurationItem(
        SyntheticEnterpriseWorld world,
        IDictionary<string, ConfigurationItem> ciBySourceKey,
        string sourceType,
        string sourceId,
        ConfigurationItem item,
        string origin = CanonicalOrigin)
    {
        var key = BuildCiSourceKey(sourceType, sourceId, origin);
        if (ciBySourceKey.ContainsKey(key))
        {
            return null;
        }

        item = item with
        {
            RtoHours = item.RtoHours ?? ResolveRtoHours(item.BusinessCriticality, item.ServiceTier),
            RpoHours = item.RpoHours ?? ResolveRpoHours(item.BusinessCriticality, item.ServiceTier)
        };

        world.ConfigurationItems.Add(item);
        ciBySourceKey[key] = item;
        return item;
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

    private static IReadOnlyList<CloudTenant> ResolveCanonicalCloudTenants(SyntheticEnterpriseWorld world, string companyId)
        => world.CloudTenants
            .Where(item => item.CompanyId == companyId)
            .GroupBy(
                tenant => $"{tenant.Provider}|{tenant.TenantType}|{tenant.Name}|{tenant.PrimaryDomain}|{tenant.Environment}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(tenant => tenant.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private string? ResolveBusinessOwnerPersonId(CompanyContext context, Department? department)
        => department is null
            ? ResolvePlatformBusinessOwner(context)
            : context.PeopleByDepartmentId.TryGetValue(department.Id, out var people)
                ? people.OrderByDescending(IsLikelyManager).ThenBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase).Select(person => person.Id).FirstOrDefault()
                : ResolvePlatformBusinessOwner(context);

    private static string? BuildFqdn(string? hostName, string? primaryDomain)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return null;
        }

        if (hostName.Contains('.', StringComparison.Ordinal))
        {
            return hostName;
        }

        if (string.IsNullOrWhiteSpace(primaryDomain))
        {
            return hostName;
        }

        return $"{hostName}.{primaryDomain}";
    }

    private static int? ResolveRtoHours(string? businessCriticality, string serviceTier)
    {
        var criticality = businessCriticality ?? string.Empty;
        return criticality switch
        {
            "MissionCritical" => 1,
            "High" => 4,
            "Medium" => serviceTier.Equals("Tier1", StringComparison.OrdinalIgnoreCase) ? 8 : 12,
            "Low" => 24,
            _ => serviceTier.Equals("Tier1", StringComparison.OrdinalIgnoreCase)
                ? 8
                : serviceTier.Equals("Tier2", StringComparison.OrdinalIgnoreCase)
                    ? 12
                    : 24
        };
    }

    private static int? ResolveRpoHours(string? businessCriticality, string serviceTier)
    {
        var criticality = businessCriticality ?? string.Empty;
        return criticality switch
        {
            "MissionCritical" => 1,
            "High" => 2,
            "Medium" => serviceTier.Equals("Tier1", StringComparison.OrdinalIgnoreCase) ? 4 : 8,
            "Low" => 24,
            _ => serviceTier.Equals("Tier1", StringComparison.OrdinalIgnoreCase)
                ? 4
                : serviceTier.Equals("Tier2", StringComparison.OrdinalIgnoreCase)
                    ? 8
                    : 24
        };
    }

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
        => criticality is "High" or "MissionCritical" ? "High" : "Medium";

    private static string ResolveIdentityStoreCriticality(IdentityStore store, Company company)
    {
        if (string.Equals(store.Environment, "Production", StringComparison.OrdinalIgnoreCase)
            && string.Equals(store.PrimaryDomain, company.PrimaryDomain, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(store.StoreType, "ActiveDirectory", StringComparison.OrdinalIgnoreCase)
                || string.Equals(store.StoreType, "HybridDirectory", StringComparison.OrdinalIgnoreCase)))
        {
            return "MissionCritical";
        }

        return string.Equals(store.Environment, "Production", StringComparison.OrdinalIgnoreCase)
            ? "High"
            : "Medium";
    }

    private static string ResolveCloudTenantCriticality(CloudTenant tenant)
        => string.Equals(tenant.Environment, "Production", StringComparison.OrdinalIgnoreCase)
            && tenant.TenantType is "Identity" or "Productivity"
                ? "High"
                : string.Equals(tenant.Environment, "Production", StringComparison.OrdinalIgnoreCase)
                    ? "Medium"
                    : "Low";

    private static string ResolveApplicationCiCriticality(ApplicationRecord application, string ciClass)
    {
        if (!string.Equals(application.Environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return application.Criticality == "High" ? "Medium" : "Low";
        }

        if (ciClass == "PlatformService"
            && (application.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase)
                || application.Name.Contains("Active Directory", StringComparison.OrdinalIgnoreCase)
                || application.Name.Contains("ERP", StringComparison.OrdinalIgnoreCase)))
        {
            return "MissionCritical";
        }

        if (string.Equals(application.Criticality, "High", StringComparison.OrdinalIgnoreCase)
            && (application.Name.Contains("ERP", StringComparison.OrdinalIgnoreCase)
                || application.Name.Contains("Warehouse", StringComparison.OrdinalIgnoreCase)
                || application.Name.Contains("Production", StringComparison.OrdinalIgnoreCase)
                || application.Name.Contains("Payroll", StringComparison.OrdinalIgnoreCase)))
        {
            return "MissionCritical";
        }

        return string.IsNullOrWhiteSpace(application.Criticality) ? "Medium" : application.Criticality;
    }

    private static string ResolveSoftwarePackageCriticality(SoftwarePackage package)
    {
        if (package.Name.Contains("CrowdStrike", StringComparison.OrdinalIgnoreCase)
            || package.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase)
            || package.Name.Contains("Sentinel", StringComparison.OrdinalIgnoreCase)
            || package.Name.Contains("VPN", StringComparison.OrdinalIgnoreCase))
        {
            return "High";
        }

        return package.Category switch
        {
            "Security" or "Identity" or "Infrastructure" => "Medium",
            "Database" or "Productivity" or "Browser" => "Low",
            _ => "Low"
        };
    }

    private static string ResolveServerCiCriticality(ServerAsset server)
    {
        if (!string.Equals(server.Environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return "Medium";
        }

        if (server.ServerRole.Contains("Domain Controller", StringComparison.OrdinalIgnoreCase)
            || server.ServerRole.Contains("SQL", StringComparison.OrdinalIgnoreCase)
            || server.ServerRole.Contains("ERP", StringComparison.OrdinalIgnoreCase)
            || server.ServerRole.Contains("Identity", StringComparison.OrdinalIgnoreCase))
        {
            return "MissionCritical";
        }

        if (server.ServerRole.Contains("Jump Host", StringComparison.OrdinalIgnoreCase)
            || server.ServerRole.Contains("File", StringComparison.OrdinalIgnoreCase)
            || server.ServerRole.Contains("Application", StringComparison.OrdinalIgnoreCase)
            || server.Criticality.Equals("High", StringComparison.OrdinalIgnoreCase))
        {
            return "High";
        }

        return "Medium";
    }

    private static string ResolveDeviceCriticality(ManagedDevice device)
    {
        if (device.DeviceType.Contains("Privileged", StringComparison.OrdinalIgnoreCase))
        {
            return "High";
        }

        return device.DeviceType.Contains("Kiosk", StringComparison.OrdinalIgnoreCase)
            || device.DeviceType.Contains("Shared", StringComparison.OrdinalIgnoreCase)
                ? "Medium"
                : "Low";
    }

    private static string ResolveNetworkCriticality(NetworkAsset asset)
        => asset.AssetType switch
        {
            "Firewall" or "Router" or "Load Balancer" or "Wireless Controller" => "High",
            "Switch" => "Medium",
            _ => "Low"
        };

    private static string ResolveTelephonyCriticality(TelephonyAsset asset)
        => asset.AssetType.Contains("Conference", StringComparison.OrdinalIgnoreCase) ? "Medium" : "Low";

    private static string ResolveDatabaseCriticality(DatabaseRepository database, IReadOnlyDictionary<string, ApplicationRecord> applicationsById)
    {
        var applicationCriticality = !string.IsNullOrWhiteSpace(database.AssociatedApplicationId)
            && applicationsById.TryGetValue(database.AssociatedApplicationId, out var application)
            ? application.Criticality
            : null;

        if (database.Sensitivity is "Restricted" or "Confidential"
            && string.Equals(applicationCriticality, "High", StringComparison.OrdinalIgnoreCase))
        {
            return "MissionCritical";
        }

        if (!string.IsNullOrWhiteSpace(applicationCriticality))
        {
            return database.Sensitivity is "Restricted" or "Confidential"
                ? "High"
                : applicationCriticality;
        }

        return database.Sensitivity switch
        {
            "Restricted" or "Confidential" => "High",
            "Internal" => "Medium",
            _ => "Low"
        };
    }

    private static string ResolveShareCriticality(FileShareRepository share)
    {
        if (share.Sensitivity is "Restricted" or "Confidential")
        {
            return "High";
        }

        return share.SharePurpose switch
        {
            "UserProfile" or "UserHome" => "Low",
            "DepartmentArchive" or "DepartmentReference" => "Medium",
            _ => "Medium"
        };
    }

    private static string ResolveCollaborationCriticality(CollaborationSite site)
    {
        if (string.Equals(site.WorkspaceType, "Executive", StringComparison.OrdinalIgnoreCase))
        {
            return "High";
        }

        if (string.Equals(site.PrivacyType, "Private", StringComparison.OrdinalIgnoreCase))
        {
            return "Medium";
        }

        return site.Platform.Contains("Teams", StringComparison.OrdinalIgnoreCase) ? "Low" : "Medium";
    }

    private static string ResolveDirectoryAccountCiClass(DirectoryAccount account)
        => account.AccountType switch
        {
            "User" => "UserAccount",
            "Secondary" => "SecondaryAccount",
            "Privileged" => "PrivilegedAccount",
            "Service" => "ServiceAccount",
            "Shared" => "SharedMailboxAccount",
            "BuiltIn" => "BuiltInAccount",
            "Device" => "MachineAccount",
            "Guest" => "GuestAccount",
            "Contractor" => "ContractorAccount",
            "ManagedServiceProvider" => "ManagedServiceProviderAccount",
            _ => "DirectoryAccount"
        };

    private static string ResolveDirectoryAccountServiceClassification(DirectoryAccount account)
    {
        if (account.Privileged)
        {
            return "PrivilegedIdentity";
        }

        return account.AccountType switch
        {
            "Service" => "ServiceIdentity",
            "Device" => "MachineIdentity",
            "Shared" => "SharedIdentity",
            "BuiltIn" => "BuiltInIdentity",
            "Guest" or "Contractor" or "ManagedServiceProvider" => "ExternalIdentity",
            _ => "EndUserIdentity"
        };
    }

    /// <summary>
    /// Reads the criticality of an account object from the object itself: what it is, whether it is
    /// privileged, the administrative tier it sits in and whether it is still enabled.
    /// </summary>
    private static string ResolveDirectoryAccountCriticality(DirectoryAccount account)
    {
        if (!account.Enabled)
        {
            return "Low";
        }

        if (account.Privileged)
        {
            return string.Equals(account.AdministrativeTier, "Tier0", StringComparison.OrdinalIgnoreCase)
                ? "MissionCritical"
                : "High";
        }

        return account.AccountType switch
        {
            "Service" or "Shared" or "BuiltIn" => "Medium",
            _ => "Low"
        };
    }

    private static string ResolveDirectoryAccountServiceTier(string criticality)
        => criticality switch
        {
            "MissionCritical" or "High" => "Tier1",
            "Medium" => "Tier2",
            _ => "Tier3"
        };

    /// <summary>
    /// The person behind the account that sponsored an invited account. It is the only party a
    /// directory records as answerable for an object it did not create for one of its own people,
    /// and it is copied from the sponsoring account rather than chosen here. Every other account
    /// class, and any sponsor the directory no longer holds a person record for, leaves this empty.
    /// </summary>
    private static string? ResolveDirectoryAccountSponsorPersonId(
        IReadOnlyDictionary<string, DirectoryAccount> accountsById,
        DirectoryAccount account)
        => !string.IsNullOrWhiteSpace(account.InvitedByAccountId)
           && accountsById.TryGetValue(account.InvitedByAccountId, out var sponsor)
            ? sponsor.PersonId
            : null;

    /// <summary>
    /// The owner text an account object carries in its own name attributes. An object with no name
    /// attributes — a service, machine, shared or built-in object — names nobody, and the absence is
    /// reported as such.
    /// </summary>
    private static string? ResolveObservedAccountHolder(DirectoryAccount account)
    {
        var givenName = account.GivenName?.Trim();
        var surname = account.Surname?.Trim();

        if (!string.IsNullOrWhiteSpace(givenName) && !string.IsNullOrWhiteSpace(surname))
        {
            return $"{givenName} {surname}";
        }

        return string.IsNullOrWhiteSpace(givenName)
            ? string.IsNullOrWhiteSpace(surname) ? null : surname
            : givenName;
    }

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

    private const string CanonicalOrigin = "Canonical";

    private static string BuildCiSourceKey(string sourceType, string sourceId, string origin = CanonicalOrigin)
        => $"{origin}|{sourceType}:{sourceId}";

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
