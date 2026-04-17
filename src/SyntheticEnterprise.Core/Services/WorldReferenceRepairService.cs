namespace SyntheticEnterprise.Core.Services;

using System.Linq;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class WorldReferenceRepairService : IWorldReferenceRepairService
{
    public WorldReferenceRepairResult Repair(SyntheticEnterpriseWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var warnings = new List<string>();
        var removedCount = 0;
        var updatedCount = 0;

        var companyIds = ToIdSet(world.Companies.Select(company => company.Id));
        var departmentIds = ToIdSet(world.Departments.Select(department => department.Id));
        var personIds = ToIdSet(world.People.Select(person => person.Id));
        var accountIds = ToIdSet(world.Accounts.Select(account => account.Id));
        var groupIds = ToIdSet(world.Groups.Select(group => group.Id));
        var softwareIds = ToIdSet(world.SoftwarePackages.Select(package => package.Id));
        var applicationIds = ToIdSet(world.Applications.Select(application => application.Id));
        var serviceIds = ToIdSet(world.ApplicationServices.Select(service => service.Id));
        var processIds = ToIdSet(world.BusinessProcesses.Select(process => process.Id));
        var tenantIds = ToIdSet(world.CloudTenants.Select(tenant => tenant.Id));
        var externalOrganizationIds = ToIdSet(world.ExternalOrganizations.Select(organization => organization.Id));
        var serverIds = ToIdSet(world.Servers.Select(server => server.Id));
        var siteIds = ToIdSet(world.CollaborationSites.Select(site => site.Id));
        var channelIds = ToIdSet(world.CollaborationChannels.Select(channel => channel.Id));
        var libraryIds = ToIdSet(world.DocumentLibraries.Select(library => library.Id));
        var folderIds = ToIdSet(world.DocumentFolders.Select(folder => folder.Id));
        var deviceIds = ToIdSet(world.Devices.Select(device => device.Id));
        var ouIds = ToIdSet(world.OrganizationalUnits.Select(ou => ou.Id));

        removedCount += RemoveInvalid(world.GroupMemberships, membership =>
                groupIds.Contains(membership.GroupId)
                && MemberExists(membership.MemberObjectId, membership.MemberObjectType, accountIds, groupIds, deviceIds, serverIds),
            "group memberships",
            warnings);

        updatedCount += NullInvalidReferences(world.People,
            person => person.EmployerOrganizationId is not null && !externalOrganizationIds.Contains(person.EmployerOrganizationId),
            person => person with { EmployerOrganizationId = null },
            "person employer references",
            warnings);
        updatedCount += NullInvalidReferences(world.People,
            person => person.SponsorPersonId is not null && !personIds.Contains(person.SponsorPersonId),
            person => person with { SponsorPersonId = null },
            "person sponsor references",
            warnings);

        removedCount += RemoveInvalid(world.ApplicationDependencies, dependency =>
                applicationIds.Contains(dependency.SourceApplicationId)
                && applicationIds.Contains(dependency.TargetApplicationId),
            "application dependencies",
            warnings);

        removedCount += RemoveInvalid(world.ApplicationServices, service =>
                applicationIds.Contains(service.ApplicationId),
            "application services",
            warnings);

        serviceIds = ToIdSet(world.ApplicationServices.Select(service => service.Id));

        removedCount += RemoveInvalid(world.ApplicationServiceDependencies, dependency =>
                serviceIds.Contains(dependency.SourceServiceId)
                && serviceIds.Contains(dependency.TargetServiceId),
            "application service dependencies",
            warnings);

        removedCount += RemoveInvalid(world.ApplicationServiceHostings, hosting =>
                serviceIds.Contains(hosting.ApplicationServiceId)
                && HostExists(hosting.HostType, hosting.HostId, serverIds, deviceIds),
            "application service hostings",
            warnings);

        removedCount += RemoveInvalid(world.ApplicationTenantLinks, link =>
                applicationIds.Contains(link.ApplicationId)
                && tenantIds.Contains(link.CloudTenantId),
            "application tenant links",
            warnings);

        removedCount += RemoveInvalid(world.ApplicationBusinessProcessLinks, link =>
                applicationIds.Contains(link.ApplicationId)
                && processIds.Contains(link.BusinessProcessId),
            "application business-process links",
            warnings);

        removedCount += RemoveInvalid(world.ApplicationCounterpartyLinks, link =>
                applicationIds.Contains(link.ApplicationId)
                && externalOrganizationIds.Contains(link.ExternalOrganizationId),
            "application counterparty links",
            warnings);

        removedCount += RemoveInvalid(world.BusinessProcessCounterpartyLinks, link =>
                processIds.Contains(link.BusinessProcessId)
                && externalOrganizationIds.Contains(link.ExternalOrganizationId),
            "business-process counterparty links",
            warnings);

        removedCount += RemoveInvalid(world.CrossTenantAccessPolicies, policy =>
                externalOrganizationIds.Contains(policy.ExternalOrganizationId),
            "cross-tenant access policies",
            warnings);

        removedCount += RemoveInvalid(world.CrossTenantAccessEvents, accessEvent =>
                accountIds.Contains(accessEvent.AccountId)
                && externalOrganizationIds.Contains(accessEvent.ExternalOrganizationId)
                && (string.IsNullOrWhiteSpace(accessEvent.ActorAccountId) || accountIds.Contains(accessEvent.ActorAccountId)),
            "cross-tenant access events",
            warnings);

        removedCount += RemoveInvalid(world.CollaborationChannels, channel =>
                siteIds.Contains(channel.CollaborationSiteId),
            "collaboration channels",
            warnings);

        channelIds = ToIdSet(world.CollaborationChannels.Select(channel => channel.Id));

        removedCount += RemoveInvalid(world.CollaborationChannelTabs, tab =>
                channelIds.Contains(tab.CollaborationChannelId)
                && TabTargetExists(tab.TargetType, tab.TargetId, siteIds, channelIds, libraryIds, applicationIds),
            "collaboration channel tabs",
            warnings);

        removedCount += RemoveInvalid(world.DocumentLibraries, library =>
                siteIds.Contains(library.CollaborationSiteId),
            "document libraries",
            warnings);

        libraryIds = ToIdSet(world.DocumentLibraries.Select(library => library.Id));

        removedCount += RemoveInvalid(world.SitePages, page =>
                siteIds.Contains(page.CollaborationSiteId)
                && OptionalExists(page.AssociatedLibraryId, libraryIds)
                && personIds.Contains(page.AuthorPersonId),
            "site pages",
            warnings);

        removedCount += RemoveInvalid(world.DocumentFolders, folder =>
                libraryIds.Contains(folder.DocumentLibraryId)
                && ParentFolderExists(folder.Id, folder.ParentFolderId, folderIds),
            "document folders",
            warnings);

        folderIds = ToIdSet(world.DocumentFolders.Select(folder => folder.Id));

        removedCount += RemoveInvalid(world.RepositoryAccessGrants, grant =>
                RepositoryExists(grant.RepositoryType, grant.RepositoryId, world)
                && PrincipalExists(grant.PrincipalType, grant.PrincipalObjectId, accountIds, groupIds),
            "repository access grants",
            warnings);

        removedCount += RemoveInvalid(world.EndpointAdministrativeAssignments, assignment =>
                EndpointExists(assignment.EndpointType, assignment.EndpointId, deviceIds, serverIds)
                && PrincipalExists(assignment.PrincipalType, assignment.PrincipalObjectId, accountIds, groupIds),
            "endpoint administrative assignments",
            warnings);

        removedCount += RemoveInvalid(world.EndpointPolicyBaselines, baseline =>
                EndpointExists(baseline.EndpointType, baseline.EndpointId, deviceIds, serverIds),
            "endpoint policy baselines",
            warnings);

        removedCount += RemoveInvalid(world.EndpointLocalGroupMembers, membership =>
                EndpointExists(membership.EndpointType, membership.EndpointId, deviceIds, serverIds)
                && LocalGroupPrincipalExists(membership, accountIds, groupIds),
            "endpoint local-group memberships",
            warnings);

        removedCount += RemoveInvalid(world.DeviceSoftwareInstallations, installation =>
                deviceIds.Contains(installation.DeviceId)
                && softwareIds.Contains(installation.SoftwareId),
            "device software installations",
            warnings);

        removedCount += RemoveInvalid(world.ServerSoftwareInstallations, installation =>
                serverIds.Contains(installation.ServerId)
                && softwareIds.Contains(installation.SoftwareId),
            "server software installations",
            warnings);

        updatedCount += NullInvalidReferences(world.Databases,
            database => database.AssociatedApplicationId is not null && !applicationIds.Contains(database.AssociatedApplicationId),
            database => database with { AssociatedApplicationId = null },
            "database application references",
            warnings);
        updatedCount += NullInvalidReferences(world.Databases,
            database => database.HostServerId is not null && !serverIds.Contains(database.HostServerId),
            database => database with { HostServerId = null },
            "database host references",
            warnings);

        updatedCount += NullInvalidReferences(world.FileShares,
            share => share.OwnerPersonId is not null && !personIds.Contains(share.OwnerPersonId),
            share => share with { OwnerPersonId = null },
            "file share owner references",
            warnings);
        updatedCount += NullInvalidReferences(world.FileShares,
            share => share.HostServerId is not null && !serverIds.Contains(share.HostServerId),
            share => share with { HostServerId = null },
            "file share host references",
            warnings);

        updatedCount += NullInvalidReferences(world.Accounts,
            account => account.PersonId is not null && !personIds.Contains(account.PersonId),
            account => account with { PersonId = null, EmployeeId = null, ManagerAccountId = null },
            "account person references",
            warnings);
        updatedCount += NullInvalidReferences(world.Accounts,
            account => account.ManagerAccountId is not null && !accountIds.Contains(account.ManagerAccountId),
            account => account with { ManagerAccountId = null },
            "account manager references",
            warnings);
        updatedCount += NullInvalidReferences(world.Accounts,
            account => account.InvitedOrganizationId is not null && !externalOrganizationIds.Contains(account.InvitedOrganizationId),
            account => account with { InvitedOrganizationId = null },
            "account invited-organization references",
            warnings);
        updatedCount += NullInvalidReferences(world.Accounts,
            account => account.InvitedByAccountId is not null && !accountIds.Contains(account.InvitedByAccountId),
            account => account with { InvitedByAccountId = null },
            "account invited-by references",
            warnings);
        updatedCount += NullInvalidReferences(world.Accounts,
            account => account.PreviousInvitedByAccountId is not null && !accountIds.Contains(account.PreviousInvitedByAccountId),
            account => account with { PreviousInvitedByAccountId = null, SponsorLastChangedAt = null },
            "account previous invited-by references",
            warnings);
        updatedCount += NullInvalidReferences(world.Accounts,
            account => !ouIds.Contains(account.OuId),
            account => account with { OuId = string.Empty, DistinguishedName = account.SamAccountName },
            "account OU references",
            warnings);

        updatedCount += NullInvalidReferences(world.Devices,
            device => device.AssignedPersonId is not null && !personIds.Contains(device.AssignedPersonId),
            device => device with { AssignedPersonId = null },
            "device person references",
            warnings);
        updatedCount += NullInvalidReferences(world.Devices,
            device => device.DirectoryAccountId is not null && !accountIds.Contains(device.DirectoryAccountId),
            device => device with { DirectoryAccountId = null },
            "device account references",
            warnings);
        updatedCount += NullInvalidReferences(world.Devices,
            device => device.AssignedOfficeId is not null && !ExistsInCollection(device.AssignedOfficeId, world.Offices.Select(office => office.Id)),
            device => device with { AssignedOfficeId = null },
            "device office references",
            warnings);
        updatedCount += NullInvalidReferences(world.Devices,
            device => device.OuId is not null && !ouIds.Contains(device.OuId),
            device => device with { OuId = null, DistinguishedName = null },
            "device OU references",
            warnings);

        updatedCount += NullInvalidReferences(world.Servers,
            server => !string.IsNullOrWhiteSpace(server.OfficeId) && !ExistsInCollection(server.OfficeId, world.Offices.Select(office => office.Id)),
            server => server with { OfficeId = string.Empty },
            "server office references",
            warnings);
        updatedCount += NullInvalidReferences(world.Servers,
            server => server.OuId is not null && !ouIds.Contains(server.OuId),
            server => server with { OuId = null, DistinguishedName = null },
            "server OU references",
            warnings);
        updatedCount += NullInvalidReferences(world.Servers,
            server => !string.IsNullOrWhiteSpace(server.OwnerTeamId) && !ExistsInCollection(server.OwnerTeamId, world.Teams.Select(team => team.Id)),
            server => server with { OwnerTeamId = string.Empty },
            "server owner-team references",
            warnings);

        updatedCount += NullInvalidReferences(world.ObservedEntitySnapshots,
            snapshot => snapshot.CompanyId is not null && !companyIds.Contains(snapshot.CompanyId),
            snapshot => snapshot with { CompanyId = string.Empty },
            "observed snapshot company references",
            warnings);
        removedCount += RemoveInvalid(world.ObservedEntitySnapshots,
                snapshot => ObservedEntityExists(
                    snapshot,
                    world,
                    accountIds,
                    applicationIds,
                    serviceIds,
                    tenantIds,
                    deviceIds,
                    serverIds),
            "observed entity snapshots",
            warnings);
        updatedCount += NullInvalidReferences(world.PluginRecords,
            record => !string.IsNullOrWhiteSpace(record.AssociatedEntityId)
                      && !PluginAssociatedEntityExists(record.AssociatedEntityType, record.AssociatedEntityId, world),
            record => record with { AssociatedEntityType = null, AssociatedEntityId = null },
            "plugin associated-entity references",
            warnings);

        return new WorldReferenceRepairResult
        {
            RemovedCount = removedCount,
            UpdatedCount = updatedCount,
            Warnings = warnings
        };
    }

    private static HashSet<string> ToIdSet(IEnumerable<string> ids)
        => new(ids.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);

    private static bool ExistsInCollection(string? id, IEnumerable<string> ids)
        => !string.IsNullOrWhiteSpace(id) && ids.Contains(id, StringComparer.OrdinalIgnoreCase);

    private static bool OptionalExists(string? id, HashSet<string> ids)
        => string.IsNullOrWhiteSpace(id) || ids.Contains(id);

    private static bool MemberExists(
        string memberObjectId,
        string memberObjectType,
        HashSet<string> accountIds,
        HashSet<string> groupIds,
        HashSet<string> deviceIds,
        HashSet<string> serverIds)
        => memberObjectType switch
        {
            "Group" => groupIds.Contains(memberObjectId),
            "Device" => deviceIds.Contains(memberObjectId),
            "Server" => serverIds.Contains(memberObjectId),
            _ => accountIds.Contains(memberObjectId)
        };

    private static bool PrincipalExists(string principalType, string principalObjectId, HashSet<string> accountIds, HashSet<string> groupIds)
        => principalType switch
        {
            "Account" => accountIds.Contains(principalObjectId),
            _ => groupIds.Contains(principalObjectId)
        };

    private static bool HostExists(string hostType, string? hostId, HashSet<string> serverIds, HashSet<string> deviceIds)
    {
        if (string.IsNullOrWhiteSpace(hostId))
        {
            return !string.Equals(hostType, "Server", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(hostType, "Device", StringComparison.OrdinalIgnoreCase);
        }

        return hostType switch
        {
            "Device" => deviceIds.Contains(hostId),
            _ => serverIds.Contains(hostId)
        };
    }

    private static bool EndpointExists(string endpointType, string endpointId, HashSet<string> deviceIds, HashSet<string> serverIds)
        => endpointType switch
        {
            "Server" => serverIds.Contains(endpointId),
            _ => deviceIds.Contains(endpointId)
        };

    private static bool LocalGroupPrincipalExists(
        EndpointLocalGroupMember membership,
        HashSet<string> accountIds,
        HashSet<string> groupIds)
    {
        if (string.IsNullOrWhiteSpace(membership.PrincipalObjectId))
        {
            return true;
        }

        return PrincipalExists(membership.PrincipalType, membership.PrincipalObjectId, accountIds, groupIds);
    }

    private static bool TabTargetExists(
        string targetType,
        string? targetId,
        HashSet<string> siteIds,
        HashSet<string> channelIds,
        HashSet<string> libraryIds,
        HashSet<string> applicationIds)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return true;
        }

        return targetType switch
        {
            "CollaborationSite" => siteIds.Contains(targetId),
            "CollaborationChannel" => channelIds.Contains(targetId),
            "DocumentLibrary" => libraryIds.Contains(targetId),
            "Application" => applicationIds.Contains(targetId),
            _ => true
        };
    }

    private static bool ParentFolderExists(string folderId, string? parentFolderId, HashSet<string> folderIds)
        => string.IsNullOrWhiteSpace(parentFolderId)
           || (!string.Equals(folderId, parentFolderId, StringComparison.OrdinalIgnoreCase) && folderIds.Contains(parentFolderId));

    private static bool RepositoryExists(string repositoryType, string repositoryId, SyntheticEnterpriseWorld world)
        => repositoryType switch
        {
            "Database" => world.Databases.Any(record => record.Id == repositoryId),
            "FileShare" => world.FileShares.Any(record => record.Id == repositoryId),
            "CollaborationSite" => world.CollaborationSites.Any(record => record.Id == repositoryId),
            "CollaborationChannel" => world.CollaborationChannels.Any(record => record.Id == repositoryId),
            "DocumentLibrary" => world.DocumentLibraries.Any(record => record.Id == repositoryId),
            "DocumentFolder" => world.DocumentFolders.Any(record => record.Id == repositoryId),
            "SitePage" => world.SitePages.Any(record => record.Id == repositoryId),
            _ => false
        };

    private static bool ObservedEntityExists(
        ObservedEntitySnapshot snapshot,
        SyntheticEnterpriseWorld world,
        HashSet<string> accountIds,
        HashSet<string> applicationIds,
        HashSet<string> serviceIds,
        HashSet<string> tenantIds,
        HashSet<string> deviceIds,
        HashSet<string> serverIds)
        => snapshot.EntityType switch
        {
            "Account" => accountIds.Contains(snapshot.EntityId),
            "Application" => applicationIds.Contains(snapshot.EntityId),
            "ApplicationService" => serviceIds.Contains(snapshot.EntityId),
            "BusinessProcess" => world.BusinessProcesses.Any(process => string.Equals(process.Id, snapshot.EntityId, StringComparison.OrdinalIgnoreCase)),
            "CloudTenant" => tenantIds.Contains(snapshot.EntityId),
            "Device" => deviceIds.Contains(snapshot.EntityId),
            "Server" => serverIds.Contains(snapshot.EntityId),
            "EndpointPolicy" or "EndpointLocalGroup" => deviceIds.Contains(snapshot.EntityId) || serverIds.Contains(snapshot.EntityId),
            "ExternalOrganization" => world.ExternalOrganizations.Any(organization => string.Equals(organization.Id, snapshot.EntityId, StringComparison.OrdinalIgnoreCase)),
            "FileShare" => world.FileShares.Any(share => string.Equals(share.Id, snapshot.EntityId, StringComparison.OrdinalIgnoreCase)),
            "CollaborationSite" => world.CollaborationSites.Any(site => string.Equals(site.Id, snapshot.EntityId, StringComparison.OrdinalIgnoreCase)),
            "DocumentLibrary" => world.DocumentLibraries.Any(library => string.Equals(library.Id, snapshot.EntityId, StringComparison.OrdinalIgnoreCase)),
            "SitePage" => world.SitePages.Any(page => string.Equals(page.Id, snapshot.EntityId, StringComparison.OrdinalIgnoreCase)),
            _ => true
        };

    private static bool PluginAssociatedEntityExists(string? associatedEntityType, string? associatedEntityId, SyntheticEnterpriseWorld world)
    {
        if (string.IsNullOrWhiteSpace(associatedEntityType) || string.IsNullOrWhiteSpace(associatedEntityId))
        {
            return true;
        }

        return associatedEntityType switch
        {
            "Database" => world.Databases.Any(database => string.Equals(database.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "FileShare" => world.FileShares.Any(share => string.Equals(share.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "CollaborationSite" => world.CollaborationSites.Any(site => string.Equals(site.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "CollaborationChannel" => world.CollaborationChannels.Any(channel => string.Equals(channel.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "DocumentLibrary" => world.DocumentLibraries.Any(library => string.Equals(library.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "DocumentFolder" => world.DocumentFolders.Any(folder => string.Equals(folder.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "SitePage" => world.SitePages.Any(page => string.Equals(page.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "Application" => world.Applications.Any(application => string.Equals(application.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "ApplicationService" => world.ApplicationServices.Any(service => string.Equals(service.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "BusinessProcess" => world.BusinessProcesses.Any(process => string.Equals(process.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "CloudTenant" => world.CloudTenants.Any(tenant => string.Equals(tenant.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "Person" => world.People.Any(person => string.Equals(person.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "Account" => world.Accounts.Any(account => string.Equals(account.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "Group" => world.Groups.Any(group => string.Equals(group.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            "ExternalOrganization" => world.ExternalOrganizations.Any(organization => string.Equals(organization.Id, associatedEntityId, StringComparison.OrdinalIgnoreCase)),
            _ => true
        };
    }

    private static int RemoveInvalid<T>(
        List<T> records,
        Func<T, bool> predicate,
        string description,
        List<string> warnings)
    {
        var removed = records.RemoveAll(record => !predicate(record));
        if (removed > 0)
        {
            warnings.Add($"Reference repair removed {removed} invalid {description}.");
        }

        return removed;
    }

    private static int NullInvalidReferences<T>(
        List<T> records,
        Func<T, bool> shouldUpdate,
        Func<T, T> updater,
        string description,
        List<string> warnings)
    {
        var updated = 0;
        for (var i = 0; i < records.Count; i++)
        {
            if (!shouldUpdate(records[i]))
            {
                continue;
            }

            records[i] = updater(records[i]);
            updated++;
        }

        if (updated > 0)
        {
            warnings.Add($"Reference repair updated {updated} invalid {description}.");
        }

        return updated;
    }
}
