namespace SyntheticEnterprise.Core.Services;

using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class WorldOwnershipReconciliationService : IWorldOwnershipReconciliationService
{
    private readonly ILayerOwnershipRegistry _layerOwnershipRegistry;

    public WorldOwnershipReconciliationService(ILayerOwnershipRegistry layerOwnershipRegistry)
    {
        _layerOwnershipRegistry = layerOwnershipRegistry;
    }

    public WorldOwnershipReconciliationResult Reconcile(SyntheticEnterpriseWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var warnings = new List<string>();
        var updatedCount = 0;
        var removedCount = 0;

        if (SupportsMergeReconciliation("ApplicationRecord"))
        {
            var applicationMap = BuildDuplicateMap(
                world.Applications,
                application => application.Id,
                application => BuildApplicationKey(application),
                "application",
                warnings);

            if (applicationMap.Count > 0)
            {
                updatedCount += UpdateRecords(world.ApplicationDependencies, dependency =>
                {
                    var updated = dependency;
                    var changed = TryMapOptional(dependency.SourceApplicationId, applicationMap, value => updated = updated with { SourceApplicationId = value! });
                    changed |= TryMapOptional(dependency.TargetApplicationId, applicationMap, value => updated = updated with { TargetApplicationId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ApplicationServices, service =>
                {
                    var updated = service;
                    var changed = TryMapOptional(service.ApplicationId, applicationMap, value => updated = updated with { ApplicationId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ApplicationTenantLinks, link =>
                {
                    var updated = link;
                    var changed = TryMapOptional(link.ApplicationId, applicationMap, value => updated = updated with { ApplicationId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ApplicationBusinessProcessLinks, link =>
                {
                    var updated = link;
                    var changed = TryMapOptional(link.ApplicationId, applicationMap, value => updated = updated with { ApplicationId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ApplicationCounterpartyLinks, link =>
                {
                    var updated = link;
                    var changed = TryMapOptional(link.ApplicationId, applicationMap, value => updated = updated with { ApplicationId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ApplicationRepositoryLinks, link =>
                {
                    var updated = link;
                    var changed = TryMapOptional(link.ApplicationId, applicationMap, value => updated = updated with { ApplicationId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.Databases, database =>
                {
                    var updated = database;
                    var changed = TryMapOptional(database.AssociatedApplicationId, applicationMap, value => updated = updated with { AssociatedApplicationId = value });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.CollaborationChannelTabs, tab =>
                {
                    if (!string.Equals(tab.TargetType, "Application", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, tab);
                    }

                    var updated = tab;
                    var changed = TryMapOptional(tab.TargetId, applicationMap, value => updated = updated with { TargetId = value });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ObservedEntitySnapshots, snapshot =>
                {
                    if (!string.Equals(snapshot.EntityType, "Application", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, snapshot);
                    }

                    var updated = snapshot;
                    var changed = TryMapOptional(snapshot.EntityId, applicationMap, value => updated = updated with { EntityId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.PluginRecords, record =>
                {
                    if (!string.Equals(record.AssociatedEntityType, "Application", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, record);
                    }

                    var updated = record;
                    var changed = TryMapOptional(record.AssociatedEntityId, applicationMap, value => updated = updated with { AssociatedEntityId = value });
                    return (changed, updated);
                });

                removedCount += RemoveMappedDuplicates(world.Applications, application => application.Id, applicationMap, "applications", warnings);
            }
        }

        if (SupportsMergeReconciliation("BusinessProcess"))
        {
            var processMap = BuildDuplicateMap(
                world.BusinessProcesses,
                process => process.Id,
                process => BuildBusinessProcessKey(process),
                "business process",
                warnings);

            if (processMap.Count > 0)
            {
                updatedCount += UpdateRecords(world.ApplicationBusinessProcessLinks, link =>
                {
                    var updated = link;
                    var changed = TryMapOptional(link.BusinessProcessId, processMap, value => updated = updated with { BusinessProcessId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.BusinessProcessCounterpartyLinks, link =>
                {
                    var updated = link;
                    var changed = TryMapOptional(link.BusinessProcessId, processMap, value => updated = updated with { BusinessProcessId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.PluginRecords, record =>
                {
                    if (!string.Equals(record.AssociatedEntityType, "BusinessProcess", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, record);
                    }

                    var updated = record;
                    var changed = TryMapOptional(record.AssociatedEntityId, processMap, value => updated = updated with { AssociatedEntityId = value });
                    return (changed, updated);
                });

                removedCount += RemoveMappedDuplicates(world.BusinessProcesses, process => process.Id, processMap, "business processes", warnings);
            }
        }

        if (SupportsMergeReconciliation("CloudTenant"))
        {
            var tenantMap = BuildDuplicateMap(
                world.CloudTenants,
                tenant => tenant.Id,
                tenant => BuildCloudTenantKey(tenant),
                "cloud tenant",
                warnings);

            if (tenantMap.Count > 0)
            {
                updatedCount += UpdateRecords(world.ApplicationTenantLinks, link =>
                {
                    var updated = link;
                    var changed = TryMapOptional(link.CloudTenantId, tenantMap, value => updated = updated with { CloudTenantId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ObservedEntitySnapshots, snapshot =>
                {
                    if (!string.Equals(snapshot.EntityType, "CloudTenant", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, snapshot);
                    }

                    var updated = snapshot;
                    var changed = TryMapOptional(snapshot.EntityId, tenantMap, value => updated = updated with { EntityId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.PluginRecords, record =>
                {
                    if (!string.Equals(record.AssociatedEntityType, "CloudTenant", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, record);
                    }

                    var updated = record;
                    var changed = TryMapOptional(record.AssociatedEntityId, tenantMap, value => updated = updated with { AssociatedEntityId = value });
                    return (changed, updated);
                });

                removedCount += RemoveMappedDuplicates(world.CloudTenants, tenant => tenant.Id, tenantMap, "cloud tenants", warnings);
            }
        }

        if (SupportsMergeReconciliation("ExternalOrganization"))
        {
            var externalOrganizationMap = BuildDuplicateMap(
                world.ExternalOrganizations,
                organization => organization.Id,
                organization => BuildExternalOrganizationKey(organization),
                "external organization",
                warnings);

            if (externalOrganizationMap.Count > 0)
            {
                updatedCount += UpdateRecords(world.People, person =>
                {
                    var updated = person;
                    var changed = TryMapOptional(person.EmployerOrganizationId, externalOrganizationMap, value => updated = updated with { EmployerOrganizationId = value });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.Accounts, account =>
                {
                    var updated = account;
                    var changed = TryMapOptional(account.InvitedOrganizationId, externalOrganizationMap, value => updated = updated with { InvitedOrganizationId = value });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ApplicationCounterpartyLinks, link =>
                {
                    var updated = link;
                    var changed = TryMapOptional(link.ExternalOrganizationId, externalOrganizationMap, value => updated = updated with { ExternalOrganizationId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.BusinessProcessCounterpartyLinks, link =>
                {
                    var updated = link;
                    var changed = TryMapOptional(link.ExternalOrganizationId, externalOrganizationMap, value => updated = updated with { ExternalOrganizationId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.CrossTenantAccessPolicies, policy =>
                {
                    var updated = policy;
                    var changed = TryMapOptional(policy.ExternalOrganizationId, externalOrganizationMap, value => updated = updated with { ExternalOrganizationId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.CrossTenantAccessEvents, accessEvent =>
                {
                    var updated = accessEvent;
                    var changed = TryMapOptional(accessEvent.ExternalOrganizationId, externalOrganizationMap, value => updated = updated with { ExternalOrganizationId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ObservedEntitySnapshots, snapshot =>
                {
                    var updated = snapshot;
                    var changed = TryMapOptional(snapshot.OwnerReference, externalOrganizationMap, value => updated = updated with { OwnerReference = value });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.PluginRecords, record =>
                {
                    if (!string.Equals(record.AssociatedEntityType, "ExternalOrganization", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, record);
                    }

                    var updated = record;
                    var changed = TryMapOptional(record.AssociatedEntityId, externalOrganizationMap, value => updated = updated with { AssociatedEntityId = value });
                    return (changed, updated);
                });

                removedCount += RemoveMappedDuplicates(world.ExternalOrganizations, organization => organization.Id, externalOrganizationMap, "external organizations", warnings);
            }
        }

        if (SupportsMergeReconciliation("CrossTenantAccessPolicyRecord"))
        {
            var policyMap = BuildDuplicateMap(
                world.CrossTenantAccessPolicies,
                policy => policy.Id,
                policy => BuildCrossTenantPolicyKey(policy),
                "cross-tenant access policy",
                warnings);

            if (policyMap.Count > 0)
            {
                updatedCount += UpdateRecords(world.CrossTenantAccessEvents, accessEvent =>
                {
                    var updated = accessEvent;
                    var changed = TryMapOptional(accessEvent.PolicyId, policyMap, value => updated = updated with { PolicyId = value });
                    return (changed, updated);
                });

                removedCount += RemoveMappedDuplicates(world.CrossTenantAccessPolicies, policy => policy.Id, policyMap, "cross-tenant access policies", warnings);
            }
        }

        if (SupportsMergeReconciliation("ApplicationService"))
        {
            var serviceMap = BuildDuplicateMap(
                world.ApplicationServices,
                service => service.Id,
                service => BuildApplicationServiceKey(service),
                "application service",
                warnings);

            if (serviceMap.Count > 0)
            {
                updatedCount += UpdateRecords(world.ApplicationServiceDependencies, dependency =>
                {
                    var updated = dependency;
                    var changed = TryMapOptional(dependency.SourceServiceId, serviceMap, value => updated = updated with { SourceServiceId = value! });
                    changed |= TryMapOptional(dependency.TargetServiceId, serviceMap, value => updated = updated with { TargetServiceId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ApplicationServiceHostings, hosting =>
                {
                    var updated = hosting;
                    var changed = TryMapOptional(hosting.ApplicationServiceId, serviceMap, value => updated = updated with { ApplicationServiceId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.ObservedEntitySnapshots, snapshot =>
                {
                    if (!string.Equals(snapshot.EntityType, "ApplicationService", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, snapshot);
                    }

                    var updated = snapshot;
                    var changed = TryMapOptional(snapshot.EntityId, serviceMap, value => updated = updated with { EntityId = value! });
                    return (changed, updated);
                });

                updatedCount += UpdateRecords(world.PluginRecords, record =>
                {
                    if (!string.Equals(record.AssociatedEntityType, "ApplicationService", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, record);
                    }

                    var updated = record;
                    var changed = TryMapOptional(record.AssociatedEntityId, serviceMap, value => updated = updated with { AssociatedEntityId = value });
                    return (changed, updated);
                });

                removedCount += RemoveMappedDuplicates(world.ApplicationServices, service => service.Id, serviceMap, "application services", warnings);
            }
        }

        DeduplicateRecords(
            world.ApplicationDependencies,
            dependency => $"{Normalize(dependency.SourceApplicationId)}|{Normalize(dependency.TargetApplicationId)}|{Normalize(dependency.DependencyType)}|{Normalize(dependency.InterfaceType)}",
            "application dependencies",
            warnings,
            ref removedCount);
        DeduplicateRecords(
            world.ApplicationServiceDependencies,
            dependency => $"{Normalize(dependency.SourceServiceId)}|{Normalize(dependency.TargetServiceId)}|{Normalize(dependency.DependencyType)}|{Normalize(dependency.InterfaceType)}",
            "application service dependencies",
            warnings,
            ref removedCount);
        DeduplicateRecords(
            world.ApplicationServiceHostings,
            hosting => $"{Normalize(hosting.ApplicationServiceId)}|{Normalize(hosting.HostType)}|{Normalize(hosting.HostId)}|{Normalize(hosting.HostName)}|{Normalize(hosting.HostingRole)}|{Normalize(hosting.DeploymentModel)}",
            "application service hostings",
            warnings,
            ref removedCount);
        DeduplicateRecords(
            world.ApplicationTenantLinks,
            link => $"{Normalize(link.ApplicationId)}|{Normalize(link.CloudTenantId)}|{Normalize(link.RelationshipType)}|{Normalize(link.IsPrimary.ToString())}",
            "application tenant links",
            warnings,
            ref removedCount);
        DeduplicateRecords(
            world.ApplicationBusinessProcessLinks,
            link => $"{Normalize(link.ApplicationId)}|{Normalize(link.BusinessProcessId)}|{Normalize(link.RelationshipType)}|{Normalize(link.IsPrimary.ToString())}",
            "application business-process links",
            warnings,
            ref removedCount);
        DeduplicateRecords(
            world.ApplicationCounterpartyLinks,
            link => $"{Normalize(link.ApplicationId)}|{Normalize(link.ExternalOrganizationId)}|{Normalize(link.RelationshipType)}|{Normalize(link.IntegrationType)}",
            "application counterparty links",
            warnings,
            ref removedCount);
        DeduplicateRecords(
            world.BusinessProcessCounterpartyLinks,
            link => $"{Normalize(link.BusinessProcessId)}|{Normalize(link.ExternalOrganizationId)}|{Normalize(link.RelationshipType)}|{Normalize(link.IsPrimary.ToString())}",
            "business-process counterparty links",
            warnings,
            ref removedCount);
        DeduplicateRecords(
            world.CrossTenantAccessEvents,
            accessEvent => $"{Normalize(accessEvent.AccountId)}|{Normalize(accessEvent.ExternalOrganizationId)}|{Normalize(accessEvent.EventType)}|{Normalize(accessEvent.EventCategory)}|{Normalize(accessEvent.PolicyId)}|{Normalize(accessEvent.ResourceReference)}|{Normalize(accessEvent.EntitlementPackageName)}|{Normalize(accessEvent.ReviewDecision)}|{Normalize(accessEvent.SourceSystem)}",
            "cross-tenant access events",
            warnings,
            ref removedCount);
        DeduplicateRecords(
            world.ObservedEntitySnapshots,
            snapshot => $"{Normalize(snapshot.EntityType)}|{Normalize(snapshot.EntityId)}|{Normalize(snapshot.SourceSystem)}|{Normalize(snapshot.DisplayName)}|{Normalize(snapshot.ObservedState)}|{Normalize(snapshot.GroundTruthState)}|{Normalize(snapshot.OwnerReference)}",
            "observed entity snapshots",
            warnings,
            ref removedCount);

        return new WorldOwnershipReconciliationResult
        {
            UpdatedCount = updatedCount,
            RemovedCount = removedCount,
            Warnings = warnings
        };
    }

    private bool SupportsMergeReconciliation(string entityType)
        => _layerOwnershipRegistry.GetOwnedArtifacts().Any(artifact =>
            string.Equals(artifact.EntityType, entityType, StringComparison.OrdinalIgnoreCase)
            && artifact.SupportsMergeReconciliation);

    private static Dictionary<string, string> BuildDuplicateMap<T>(
        IEnumerable<T> records,
        Func<T, string> idSelector,
        Func<T, string?> keySelector,
        string recordType,
        List<string> warnings)
    {
        var canonicalByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguousKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var id = idSelector(record);
            var key = keySelector(record);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (ambiguousKeys.Contains(key))
            {
                continue;
            }

            if (!canonicalByKey.TryGetValue(key, out var canonicalId))
            {
                canonicalByKey[key] = id;
                continue;
            }

            if (string.Equals(canonicalId, id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            map[id] = canonicalId;
        }

        if (ambiguousKeys.Count > 0)
        {
            warnings.Add($"Ownership reconciliation skipped {ambiguousKeys.Count} ambiguous {recordType} key(s).");
        }

        return map;
    }

    private static string? BuildApplicationKey(ApplicationRecord application)
        => string.IsNullOrWhiteSpace(application.Name)
            ? null
            : $"{application.CompanyId}|{Normalize(application.Name)}|{Normalize(application.Vendor)}|{Normalize(application.Environment)}";

    private static string? BuildBusinessProcessKey(BusinessProcess process)
        => string.IsNullOrWhiteSpace(process.Name)
            ? null
            : $"{process.CompanyId}|{Normalize(process.Name)}";

    private static string? BuildCloudTenantKey(CloudTenant tenant)
        => string.IsNullOrWhiteSpace(tenant.PrimaryDomain) && string.IsNullOrWhiteSpace(tenant.Name)
            ? null
            : $"{tenant.CompanyId}|{Normalize(tenant.Provider)}|{Normalize(tenant.TenantType)}|{Normalize(tenant.PrimaryDomain)}|{Normalize(tenant.Name)}|{Normalize(tenant.Environment)}";

    private static string? BuildExternalOrganizationKey(ExternalOrganization organization)
    {
        var identity = !string.IsNullOrWhiteSpace(organization.PrimaryDomain)
            ? organization.PrimaryDomain
            : !string.IsNullOrWhiteSpace(organization.ContactEmail) && organization.ContactEmail.Contains('@')
                ? organization.ContactEmail[(organization.ContactEmail.IndexOf('@') + 1)..]
                : organization.Name;

        return string.IsNullOrWhiteSpace(identity)
            ? null
            : $"{organization.CompanyId}|{Normalize(organization.RelationshipType)}|{Normalize(identity)}|{Normalize(organization.Country)}";
    }

    private static string? BuildCrossTenantPolicyKey(CrossTenantAccessPolicyRecord policy)
        => string.IsNullOrWhiteSpace(policy.PolicyName)
            ? null
            : $"{policy.CompanyId}|{Normalize(policy.ExternalOrganizationId)}|{Normalize(policy.PolicyName)}|{Normalize(policy.AccessDirection)}|{Normalize(policy.ResourceTenantDomain)}|{Normalize(policy.HomeTenantDomain)}";

    private static string? BuildApplicationServiceKey(ApplicationService service)
        => string.IsNullOrWhiteSpace(service.Name)
            ? null
            : $"{service.CompanyId}|{Normalize(service.ApplicationId)}|{Normalize(service.Name)}|{Normalize(service.ServiceType)}|{Normalize(service.Runtime)}|{Normalize(service.Environment)}";

    private static bool TryMapOptional(string? sourceId, IReadOnlyDictionary<string, string> map, Action<string> apply)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || !map.TryGetValue(sourceId, out var mappedId))
        {
            return false;
        }

        apply(mappedId);
        return true;
    }

    private static int UpdateRecords<T>(List<T> records, Func<T, (bool Changed, T Updated)> updater)
    {
        var updatedCount = 0;
        for (var i = 0; i < records.Count; i++)
        {
            var (changed, updated) = updater(records[i]);
            if (!changed)
            {
                continue;
            }

            records[i] = updated;
            updatedCount++;
        }

        return updatedCount;
    }

    private static int RemoveMappedDuplicates<T>(
        List<T> records,
        Func<T, string> idSelector,
        IReadOnlyDictionary<string, string> duplicateMap,
        string description,
        List<string> warnings)
    {
        var removed = records.RemoveAll(record =>
        {
            var id = idSelector(record);
            return !string.IsNullOrWhiteSpace(id) && duplicateMap.ContainsKey(id);
        });

        if (removed > 0)
        {
            warnings.Add($"Ownership reconciliation removed {removed} duplicate {description}.");
        }

        return removed;
    }

    private static void DeduplicateRecords<T>(
        List<T> records,
        Func<T, string> keySelector,
        string description,
        List<string> warnings,
        ref int removedCount)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = records.RemoveAll(record => !seen.Add(keySelector(record)));
        if (removed > 0)
        {
            removedCount += removed;
            warnings.Add($"Ownership reconciliation removed {removed} duplicate {description}.");
        }
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
}
