namespace SyntheticEnterprise.Core.Generation;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;

public sealed class LayerProcessor : ILayerProcessor
{
    private readonly IIdentityGenerator _identityGenerator;
    private readonly IInfrastructureGenerator _infrastructureGenerator;
    private readonly IRepositoryGenerator _repositoryGenerator;
    private readonly IEnumerable<IAnomalyInjector> _anomalyInjectors;
    private readonly IWorldCloner _worldCloner;
    private readonly ICatalogContextResolver _catalogContextResolver;
    private readonly ILayerOwnershipRegistry _layerOwnershipRegistry;
    private readonly IWorldLayerRemapService _worldLayerRemapService;
    private readonly IWorldOwnershipReconciliationService _worldOwnershipReconciliationService;
    private readonly IWorldReferenceRepairService _worldReferenceRepairService;
    private readonly IWorldQualityAuditService _worldQualityAuditService;

    public LayerProcessor(
        IIdentityGenerator identityGenerator,
        IInfrastructureGenerator infrastructureGenerator,
        IRepositoryGenerator repositoryGenerator,
        IEnumerable<IAnomalyInjector> anomalyInjectors,
        IWorldCloner worldCloner,
        ICatalogContextResolver catalogContextResolver,
        ILayerOwnershipRegistry layerOwnershipRegistry,
        IWorldLayerRemapService worldLayerRemapService,
        IWorldOwnershipReconciliationService worldOwnershipReconciliationService,
        IWorldReferenceRepairService worldReferenceRepairService,
        IWorldQualityAuditService worldQualityAuditService)
    {
        _identityGenerator = identityGenerator;
        _infrastructureGenerator = infrastructureGenerator;
        _repositoryGenerator = repositoryGenerator;
        _anomalyInjectors = anomalyInjectors;
        _worldCloner = worldCloner;
        _catalogContextResolver = catalogContextResolver;
        _layerOwnershipRegistry = layerOwnershipRegistry;
        _worldLayerRemapService = worldLayerRemapService;
        _worldOwnershipReconciliationService = worldOwnershipReconciliationService;
        _worldReferenceRepairService = worldReferenceRepairService;
        _worldQualityAuditService = worldQualityAuditService;
    }

    public GenerationResult AddIdentityLayer(GenerationResult input, LayerProcessingOptions? options = null)
    {
        options ??= new LayerProcessingOptions();
        var result = _worldCloner.Clone(input);
        var mode = options.IdentityMode;

        if (mode == LayerRegenerationMode.SkipIfPresent && HasIdentityData(result))
        {
            return AppendWarning(result, "Identity layer already present; skipped.");
        }

        if (mode == LayerRegenerationMode.ReplaceLayer)
        {
            ClearIdentityData(result);
        }

        CatalogSet catalogs;
        if (mode == LayerRegenerationMode.Merge)
        {
            var staging = _worldCloner.Clone(input);
            ClearIdentityData(staging);
            catalogs = _catalogContextResolver.Resolve(staging);
            var context = BuildContext(staging);
            _identityGenerator.GenerateIdentity(staging.World, context, catalogs);
            MergeIdentityLayerArtifacts(result, input, staging);
        }
        else
        {
            catalogs = _catalogContextResolver.Resolve(result);
            var context = BuildContext(result);
            _identityGenerator.GenerateIdentity(result.World, context, catalogs);
        }

        result = UpdateCatalogMetadata(result, catalogs);
        RefreshOwnershipMetadata(result);
        MarkLayerApplied(result, "Identity");
        result = ApplyLayerContinuity(input, result, mode, "Identity");
        result = ApplyReferenceRepair(result);
        return RefreshStatistics(result);
    }

    public GenerationResult AddInfrastructureLayer(GenerationResult input, LayerProcessingOptions? options = null)
    {
        options ??= new LayerProcessingOptions();
        var result = _worldCloner.Clone(input);
        var mode = options.InfrastructureMode;

        if (mode == LayerRegenerationMode.SkipIfPresent && HasInfrastructureData(result))
        {
            return AppendWarning(result, "Infrastructure layer already present; skipped.");
        }

        if (mode == LayerRegenerationMode.ReplaceLayer)
        {
            ClearInfrastructureData(result);
        }

        CatalogSet catalogs;
        if (mode == LayerRegenerationMode.Merge)
        {
            var staging = _worldCloner.Clone(input);
            var preservedSoftwarePackages = staging.World.SoftwarePackages.ToList();
            var preserveExistingSoftwareInstallations =
                staging.World.DeviceSoftwareInstallations.Count > 0 ||
                staging.World.ServerSoftwareInstallations.Count > 0;
            var preserveExistingEndpointControlState =
                staging.World.EndpointAdministrativeAssignments.Count > 0 ||
                staging.World.EndpointPolicyBaselines.Count > 0 ||
                staging.World.EndpointLocalGroupMembers.Count > 0;
            ClearInfrastructureData(staging);
            if (preservedSoftwarePackages.Count > 0)
            {
                staging.World.SoftwarePackages.AddRange(preservedSoftwarePackages);
            }

            catalogs = _catalogContextResolver.Resolve(staging);
            var context = BuildContext(staging);
            _infrastructureGenerator.GenerateInfrastructure(staging.World, context, catalogs);

            if (preservedSoftwarePackages.Count > 0)
            {
                staging.World.SoftwarePackages.Clear();
            }

            if (preserveExistingSoftwareInstallations)
            {
                staging.World.DeviceSoftwareInstallations.Clear();
                staging.World.ServerSoftwareInstallations.Clear();
            }

            if (preserveExistingEndpointControlState)
            {
                staging.World.EndpointAdministrativeAssignments.Clear();
                staging.World.EndpointPolicyBaselines.Clear();
                staging.World.EndpointLocalGroupMembers.Clear();
            }

            MergeInfrastructureLayerArtifacts(result, staging);
        }
        else
        {
            catalogs = _catalogContextResolver.Resolve(result);
            var context = BuildContext(result);
            _infrastructureGenerator.GenerateInfrastructure(result.World, context, catalogs);
        }

        result = UpdateCatalogMetadata(result, catalogs);
        RefreshOwnershipMetadata(result);
        MarkLayerApplied(result, "Infrastructure");
        result = ApplyLayerContinuity(input, result, mode, "Infrastructure");
        result = ApplyReferenceRepair(result);
        return RefreshStatistics(result);
    }

    public GenerationResult AddRepositoryLayer(GenerationResult input, LayerProcessingOptions? options = null)
    {
        options ??= new LayerProcessingOptions();
        var result = _worldCloner.Clone(input);
        var mode = options.RepositoryMode;

        if (mode == LayerRegenerationMode.SkipIfPresent && HasRepositoryData(result))
        {
            return AppendWarning(result, "Repository layer already present; skipped.");
        }

        if (mode == LayerRegenerationMode.ReplaceLayer)
        {
            ClearRepositoryData(result);
        }

        CatalogSet catalogs;
        if (mode == LayerRegenerationMode.Merge)
        {
            var staging = _worldCloner.Clone(input);
            var preserveExistingRepositoryLinks = staging.World.ApplicationRepositoryLinks.Count > 0;
            var preserveExistingAccessGrants = staging.World.RepositoryAccessGrants.Count > 0;
            var preserveExistingCollaborationContent =
                staging.World.CollaborationChannels.Count > 0 ||
                staging.World.CollaborationChannelTabs.Count > 0 ||
                staging.World.DocumentLibraries.Count > 0 ||
                staging.World.SitePages.Count > 0 ||
                staging.World.DocumentFolders.Count > 0;

            ClearRepositoryData(staging);
            catalogs = _catalogContextResolver.Resolve(staging);
            var context = BuildContext(staging);
            _repositoryGenerator.GenerateRepositories(staging.World, context, catalogs);

            if (preserveExistingRepositoryLinks)
            {
                staging.World.ApplicationRepositoryLinks.Clear();
            }

            if (preserveExistingAccessGrants)
            {
                staging.World.RepositoryAccessGrants.Clear();
            }

            if (preserveExistingCollaborationContent)
            {
                staging.World.CollaborationChannels.Clear();
                staging.World.CollaborationChannelTabs.Clear();
                staging.World.DocumentLibraries.Clear();
                staging.World.SitePages.Clear();
                staging.World.DocumentFolders.Clear();
            }

            MergeRepositoryLayerArtifacts(result, staging);
        }
        else
        {
            catalogs = _catalogContextResolver.Resolve(result);
            var context = BuildContext(result);
            _repositoryGenerator.GenerateRepositories(result.World, context, catalogs);
        }

        result = UpdateCatalogMetadata(result, catalogs);
        RefreshOwnershipMetadata(result);
        MarkLayerApplied(result, "Repository");
        result = ApplyLayerContinuity(input, result, mode, "Repository");
        result = ApplyReferenceRepair(result);
        return RefreshStatistics(result);
    }

    public GenerationResult ApplyAnomalyProfiles(GenerationResult input, LayerProcessingOptions? options = null)
    {
        options ??= new LayerProcessingOptions();
        var result = _worldCloner.Clone(input);
        var catalogs = _catalogContextResolver.Resolve(result);
        var context = BuildContext(result);

        foreach (var anomalyProfile in context.Scenario.Anomalies)
        {
            var profileKey = $"{anomalyProfile.Category}:{anomalyProfile.Name}";
            if (options.ApplyAnomaliesIdempotently &&
                result.WorldMetadata is not null &&
                result.WorldMetadata.AppliedAnomalyProfiles.Contains(profileKey))
            {
                result = AppendWarning(result, $"Anomaly profile already applied; skipped: {profileKey}");
                continue;
            }

            foreach (var injector in _anomalyInjectors)
            {
                injector.Apply(result.World, context, catalogs, anomalyProfile);
            }

            if (result.WorldMetadata is not null)
            {
                result.WorldMetadata.AppliedAnomalyProfiles.Add(profileKey);
            }
        }

        result = UpdateCatalogMetadata(result, catalogs);
        RefreshOwnershipMetadata(result);
        return RefreshStatistics(result);
    }

    private static GenerationContext BuildContext(GenerationResult input)
    {
        return new GenerationContext
        {
            Scenario = input.WorldMetadata?.Scenario ?? new SyntheticEnterprise.Contracts.Configuration.ScenarioDefinition(),
            Seed = input.WorldMetadata?.Seed,
            GeneratedAt = input.WorldMetadata?.GeneratedAt ?? DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string?>
            {
                ["CatalogRootPath"] = input.WorldMetadata?.CatalogRootPath,
                ["GeneratedAt"] = input.WorldMetadata?.GeneratedAt.ToString("O")
            }
        };
    }

    private static GenerationResult UpdateCatalogMetadata(GenerationResult input, CatalogSet catalogs)
    {
        input = input with { Catalogs = catalogs };

        if (input.WorldMetadata is not null)
        {
            input.WorldMetadata.CatalogKeys.Clear();
            foreach (var key in catalogs.CsvCatalogs.Keys.Concat(catalogs.JsonCatalogs.Keys))
            {
                input.WorldMetadata.CatalogKeys.Add(key);
            }
        }

        return input;
    }

    private void RefreshOwnershipMetadata(GenerationResult input)
    {
        if (input.WorldMetadata is null)
        {
            return;
        }

        input.WorldMetadata.OwnedArtifacts.Clear();
        input.WorldMetadata.OwnedArtifacts.AddRange(_layerOwnershipRegistry.GetOwnedArtifacts());
    }

    private static bool HasIdentityData(GenerationResult input) =>
        input.World.Accounts.Count > 0 ||
        input.World.Groups.Count > 0 ||
        input.World.OrganizationalUnits.Count > 0 ||
        input.World.GroupMemberships.Count > 0 ||
        input.World.People.Any(person => !string.Equals(person.EmploymentType, "Employee", StringComparison.OrdinalIgnoreCase));

    private static bool HasInfrastructureData(GenerationResult input) =>
        input.World.Devices.Count > 0 ||
        input.World.Servers.Count > 0 ||
        input.World.NetworkAssets.Count > 0 ||
        input.World.TelephonyAssets.Count > 0;

    private static bool HasRepositoryData(GenerationResult input) =>
        input.World.Databases.Count > 0 ||
        input.World.FileShares.Count > 0 ||
        input.World.CollaborationSites.Count > 0 ||
        input.World.CollaborationChannels.Count > 0 ||
        input.World.CollaborationChannelTabs.Count > 0 ||
        input.World.DocumentLibraries.Count > 0 ||
        input.World.SitePages.Count > 0 ||
        input.World.DocumentFolders.Count > 0 ||
        input.World.ApplicationRepositoryLinks.Count > 0 ||
        input.World.RepositoryAccessGrants.Count > 0;

    private static void ClearIdentityData(GenerationResult input)
    {
        input.World.OrganizationalUnits.Clear();
        input.World.Accounts.Clear();
        input.World.Groups.Clear();
        input.World.GroupMemberships.Clear();
        input.World.IdentityAnomalies.Clear();
        input.World.CrossTenantAccessPolicies.Clear();
        input.World.CrossTenantAccessEvents.Clear();
        input.World.People.RemoveAll(person => !string.Equals(person.EmploymentType, "Employee", StringComparison.OrdinalIgnoreCase));
    }

    private static void MergeIdentityLayerArtifacts(
        GenerationResult target,
        GenerationResult previous,
        GenerationResult generated)
    {
        var existingExternalOrganizationIds = new HashSet<string>(
            previous.World.ExternalOrganizations.Select(organization => organization.Id),
            StringComparer.OrdinalIgnoreCase);

        target.World.People.AddRange(
            generated.World.People.Where(person =>
                !string.Equals(person.EmploymentType, "Employee", StringComparison.OrdinalIgnoreCase)));
        target.World.OrganizationalUnits.AddRange(generated.World.OrganizationalUnits);
        target.World.Accounts.AddRange(generated.World.Accounts);
        target.World.Groups.AddRange(generated.World.Groups);
        target.World.GroupMemberships.AddRange(generated.World.GroupMemberships);
        target.World.IdentityAnomalies.AddRange(generated.World.IdentityAnomalies);
        target.World.CrossTenantAccessPolicies.AddRange(generated.World.CrossTenantAccessPolicies);
        target.World.CrossTenantAccessEvents.AddRange(generated.World.CrossTenantAccessEvents);
        target.World.ExternalOrganizations.AddRange(
            generated.World.ExternalOrganizations.Where(organization =>
                !existingExternalOrganizationIds.Contains(organization.Id)));
    }

    private static void MergeInfrastructureLayerArtifacts(
        GenerationResult target,
        GenerationResult generated)
    {
        target.World.Devices.AddRange(generated.World.Devices);
        target.World.Servers.AddRange(generated.World.Servers);
        target.World.NetworkAssets.AddRange(generated.World.NetworkAssets);
        target.World.TelephonyAssets.AddRange(generated.World.TelephonyAssets);
        target.World.SoftwarePackages.AddRange(generated.World.SoftwarePackages);
        target.World.DeviceSoftwareInstallations.AddRange(generated.World.DeviceSoftwareInstallations);
        target.World.ServerSoftwareInstallations.AddRange(generated.World.ServerSoftwareInstallations);
        target.World.EndpointAdministrativeAssignments.AddRange(generated.World.EndpointAdministrativeAssignments);
        target.World.EndpointPolicyBaselines.AddRange(generated.World.EndpointPolicyBaselines);
        target.World.EndpointLocalGroupMembers.AddRange(generated.World.EndpointLocalGroupMembers);
        target.World.InfrastructureAnomalies.AddRange(generated.World.InfrastructureAnomalies);
    }

    private static void MergeRepositoryLayerArtifacts(
        GenerationResult target,
        GenerationResult generated)
    {
        target.World.Databases.AddRange(generated.World.Databases);
        target.World.FileShares.AddRange(generated.World.FileShares);
        target.World.CollaborationSites.AddRange(generated.World.CollaborationSites);
        target.World.CollaborationChannels.AddRange(generated.World.CollaborationChannels);
        target.World.CollaborationChannelTabs.AddRange(generated.World.CollaborationChannelTabs);
        target.World.DocumentLibraries.AddRange(generated.World.DocumentLibraries);
        target.World.SitePages.AddRange(generated.World.SitePages);
        target.World.DocumentFolders.AddRange(generated.World.DocumentFolders);
        target.World.ApplicationRepositoryLinks.AddRange(generated.World.ApplicationRepositoryLinks);
        target.World.RepositoryAccessGrants.AddRange(generated.World.RepositoryAccessGrants);
        target.World.RepositoryAnomalies.AddRange(generated.World.RepositoryAnomalies);
    }

    private static void ClearInfrastructureData(GenerationResult input)
    {
        input.World.Devices.Clear();
        input.World.Servers.Clear();
        input.World.NetworkAssets.Clear();
        input.World.TelephonyAssets.Clear();
        input.World.SoftwarePackages.Clear();
        input.World.DeviceSoftwareInstallations.Clear();
        input.World.ServerSoftwareInstallations.Clear();
        input.World.EndpointAdministrativeAssignments.Clear();
        input.World.EndpointPolicyBaselines.Clear();
        input.World.EndpointLocalGroupMembers.Clear();
        input.World.InfrastructureAnomalies.Clear();
    }

    private static void ClearRepositoryData(GenerationResult input)
    {
        input.World.Databases.Clear();
        input.World.FileShares.Clear();
        input.World.CollaborationSites.Clear();
        input.World.CollaborationChannels.Clear();
        input.World.CollaborationChannelTabs.Clear();
        input.World.DocumentLibraries.Clear();
        input.World.SitePages.Clear();
        input.World.DocumentFolders.Clear();
        input.World.ApplicationRepositoryLinks.Clear();
        input.World.RepositoryAccessGrants.Clear();
        input.World.RepositoryAnomalies.Clear();
    }

    private static void MarkLayerApplied(GenerationResult input, string layer)
    {
        input.WorldMetadata?.AppliedLayers.Add(layer);
    }

    private static GenerationResult RefreshStatistics(GenerationResult input)
    {
        return input with
        {
            Statistics = new GenerationStatistics
            {
                CompanyCount = input.World.Companies.Count,
                OfficeCount = input.World.Offices.Count,
                PersonCount = input.World.People.Count,
                AccountCount = input.World.Accounts.Count,
                GroupCount = input.World.Groups.Count,
                ApplicationCount = input.World.Applications.Count,
                DeviceCount = input.World.Devices.Count + input.World.Servers.Count,
                RepositoryCount = input.World.Databases.Count
                    + input.World.FileShares.Count
                    + input.World.CollaborationSites.Count
                    + input.World.CollaborationChannels.Count
                    + input.World.CollaborationChannelTabs.Count
                    + input.World.DocumentLibraries.Count
                    + input.World.SitePages.Count
                    + input.World.DocumentFolders.Count
            }
        };
    }

    private static GenerationResult AppendWarning(GenerationResult input, string warning)
    {
        var warnings = input.Warnings.ToList();
        warnings.Add(warning);
        return input with { Warnings = warnings };
    }

    private GenerationResult ApplyLayerContinuity(
        GenerationResult previous,
        GenerationResult current,
        LayerRegenerationMode mode,
        string layer)
    {
        if (mode is not LayerRegenerationMode.ReplaceLayer and not LayerRegenerationMode.Merge)
        {
            return current;
        }

        var remapResult = (layer, mode) switch
        {
            ("Identity", LayerRegenerationMode.ReplaceLayer) => _worldLayerRemapService.RemapAfterIdentityReplacement(previous.World, current.World),
            ("Identity", LayerRegenerationMode.Merge) => _worldLayerRemapService.MergeAfterIdentityRegeneration(previous.World, current.World),
            ("Infrastructure", LayerRegenerationMode.ReplaceLayer) => _worldLayerRemapService.RemapAfterInfrastructureReplacement(previous.World, current.World),
            ("Infrastructure", LayerRegenerationMode.Merge) => _worldLayerRemapService.MergeAfterInfrastructureRegeneration(previous.World, current.World),
            ("Repository", LayerRegenerationMode.ReplaceLayer) => _worldLayerRemapService.RemapAfterRepositoryReplacement(previous.World, current.World),
            ("Repository", LayerRegenerationMode.Merge) => _worldLayerRemapService.MergeAfterRepositoryRegeneration(previous.World, current.World),
            _ => new WorldLayerRemapResult()
        };

        if (remapResult.Warnings.Count == 0)
        {
            return current;
        }

        var warnings = current.Warnings.ToList();
        warnings.AddRange(remapResult.Warnings);
        return current with { Warnings = warnings };
    }

    private GenerationResult ApplyReferenceRepair(GenerationResult input)
    {
        var ownershipResult = _worldOwnershipReconciliationService.Reconcile(input.World);
        var repairResult = _worldReferenceRepairService.Repair(input.World);
        var auditWarnings = _worldQualityAuditService.Audit(input.World).Warnings;
        if (ownershipResult.Warnings.Count == 0 && repairResult.Warnings.Count == 0 && auditWarnings.Count == 0)
        {
            return input;
        }

        var warnings = input.Warnings.ToList();
        warnings.AddRange(ownershipResult.Warnings);
        warnings.AddRange(repairResult.Warnings);
        warnings.AddRange(auditWarnings);
        return input with { Warnings = warnings };
    }
}
