namespace SyntheticEnterprise.Core.Services;

using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class WorldLayerRemapService : IWorldLayerRemapService
{
    public WorldLayerRemapResult RemapAfterIdentityReplacement(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld)
    {
        ArgumentNullException.ThrowIfNull(previousWorld);
        ArgumentNullException.ThrowIfNull(currentWorld);

        var warnings = new List<string>();
        var updatedCount = PreserveIdentityStableIdentifiers(previousWorld, currentWorld, warnings);

        var currentOrganizationalUnitsById = currentWorld.OrganizationalUnits
            .Where(ou => !string.IsNullOrWhiteSpace(ou.Id))
            .ToDictionary(ou => ou.Id, StringComparer.OrdinalIgnoreCase);
        var currentAccountsById = currentWorld.Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Id))
            .ToDictionary(account => account.Id, StringComparer.OrdinalIgnoreCase);

        var personMap = BuildIdMap(
            previousWorld.People,
            currentWorld.People,
            person => person.Id,
            BuildPersonKey,
            "person",
            warnings);
        var accountMap = BuildIdMap(
            previousWorld.Accounts,
            currentWorld.Accounts,
            account => account.Id,
            account => BuildAccountKey(account, previousWorld.People, currentWorld.People),
            "account",
            warnings);
        var groupMap = BuildIdMap(
            previousWorld.Groups,
            currentWorld.Groups,
            group => group.Id,
            group => $"{group.CompanyId}|{Normalize(group.Name)}",
            "group",
            warnings);
        var ouMap = BuildIdMap(
            previousWorld.OrganizationalUnits,
            currentWorld.OrganizationalUnits,
            ou => ou.Id,
            ou => $"{ou.CompanyId}|{Normalize(ou.DistinguishedName)}",
            "organizational unit",
            warnings);

        updatedCount += UpdateRecords(currentWorld.Devices, device =>
        {
            var updated = device;
            var changed = false;

            changed |= TryMapOptional(device.AssignedPersonId, personMap, value => updated = updated with { AssignedPersonId = value });
            changed |= TryMapOptional(device.DirectoryAccountId, accountMap, value => updated = updated with { DirectoryAccountId = value });
            changed |= TryMapOptional(device.OuId, ouMap, value =>
            {
                currentOrganizationalUnitsById.TryGetValue(value, out var targetOu);
                updated = updated with
                {
                    OuId = value,
                    DistinguishedName = targetOu is null ? updated.DistinguishedName : $"CN={device.Hostname},{targetOu.DistinguishedName}"
                };
            });

            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.Servers, server =>
        {
            var updated = server;
            var changed = false;

            changed |= TryMapOptional(server.OuId, ouMap, value =>
            {
                currentOrganizationalUnitsById.TryGetValue(value, out var targetOu);
                updated = updated with
                {
                    OuId = value,
                    DistinguishedName = targetOu is null ? updated.DistinguishedName : $"CN={server.Hostname},{targetOu.DistinguishedName}"
                };
            });

            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.TelephonyAssets, asset =>
        {
            var updated = asset;
            var changed = TryMapOptional(asset.AssignedPersonId, personMap, value => updated = updated with { AssignedPersonId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.FileShares, share =>
        {
            var updated = share;
            var changed = TryMapOptional(share.OwnerPersonId, personMap, value => updated = updated with { OwnerPersonId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.CollaborationSites, site =>
        {
            var updated = site;
            var changed = TryMapRequired(site.OwnerPersonId, personMap, value => updated = updated with { OwnerPersonId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.SitePages, page =>
        {
            var updated = page;
            var changed = TryMapRequired(page.AuthorPersonId, personMap, value => updated = updated with { AuthorPersonId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
        {
            var updated = grant;
            var changed = string.Equals(grant.PrincipalType, "Account", StringComparison.OrdinalIgnoreCase)
                ? TryMapRequired(grant.PrincipalObjectId, accountMap, value => updated = updated with { PrincipalObjectId = value })
                : TryMapRequired(grant.PrincipalObjectId, groupMap, value => updated = updated with { PrincipalObjectId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.GroupMemberships, membership =>
        {
            var updated = membership;
            var changed = string.Equals(membership.MemberObjectType, "Group", StringComparison.OrdinalIgnoreCase)
                ? TryMapRequired(membership.MemberObjectId, groupMap, value => updated = updated with { MemberObjectId = value })
                : string.Equals(membership.MemberObjectType, "Account", StringComparison.OrdinalIgnoreCase)
                    ? TryMapRequired(membership.MemberObjectId, accountMap, value => updated = updated with { MemberObjectId = value })
                    : false;

            changed |= TryMapRequired(membership.GroupId, groupMap, value => updated = updated with { GroupId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.EndpointAdministrativeAssignments, assignment =>
        {
            var updated = assignment;
            var changed = string.Equals(assignment.PrincipalType, "Account", StringComparison.OrdinalIgnoreCase)
                ? TryMapRequired(assignment.PrincipalObjectId, accountMap, value => updated = updated with { PrincipalObjectId = value })
                : TryMapRequired(assignment.PrincipalObjectId, groupMap, value => updated = updated with { PrincipalObjectId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.EndpointLocalGroupMembers, membership =>
        {
            var updated = membership;
            var changed = false;

            if (!string.IsNullOrWhiteSpace(membership.PrincipalObjectId))
            {
                changed = string.Equals(membership.PrincipalType, "Account", StringComparison.OrdinalIgnoreCase)
                    ? TryMapRequired(membership.PrincipalObjectId, accountMap, value => updated = updated with { PrincipalObjectId = value })
                    : TryMapRequired(membership.PrincipalObjectId, groupMap, value => updated = updated with { PrincipalObjectId = value });
            }

            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.CrossTenantAccessEvents, accessEvent =>
        {
            var updated = accessEvent;
            var changed = TryMapRequired(accessEvent.AccountId, accountMap, value => updated = updated with { AccountId = value });
            changed |= TryMapOptional(accessEvent.ActorAccountId, accountMap, value => updated = updated with { ActorAccountId = value });
            return (changed, updated);
        });

        DeduplicateRecords(
            currentWorld.GroupMemberships,
            membership => $"{Normalize(membership.GroupId)}|{Normalize(membership.MemberObjectType)}|{Normalize(membership.MemberObjectId)}",
            "group memberships",
            warnings);
        DeduplicateRecords(
            currentWorld.EndpointAdministrativeAssignments,
            assignment => $"{Normalize(assignment.EndpointType)}|{Normalize(assignment.EndpointId)}|{Normalize(assignment.PrincipalType)}|{Normalize(assignment.PrincipalObjectId)}|{Normalize(assignment.AccessRole)}",
            "endpoint administrative assignments",
            warnings);
        DeduplicateRecords(
            currentWorld.EndpointLocalGroupMembers,
            membership => $"{Normalize(membership.EndpointType)}|{Normalize(membership.EndpointId)}|{Normalize(membership.LocalGroupName)}|{Normalize(membership.PrincipalType)}|{Normalize(membership.PrincipalObjectId)}|{Normalize(membership.PrincipalName)}",
            "endpoint local-group memberships",
            warnings);

        updatedCount += UpdateRecords(currentWorld.ObservedEntitySnapshots, snapshot =>
        {
            var updated = snapshot;
            var changed = false;

            switch (snapshot.EntityType)
            {
                case "Account":
                    changed |= TryMapRequired(snapshot.EntityId, accountMap, value =>
                    {
                        updated = updated with
                        {
                            EntityId = value,
                            DisplayName = currentAccountsById.TryGetValue(value, out var account)
                                ? account.UserPrincipalName
                                : updated.DisplayName
                        };
                    });
                    changed |= TryMapOptional(snapshot.OwnerReference, accountMap, value => updated = updated with { OwnerReference = value });
                    break;
                case "Device":
                    changed |= TryMapOptional(snapshot.OwnerReference, accountMap, value => updated = updated with { OwnerReference = value });
                    break;
                case "FileShare":
                case "CollaborationSite":
                    changed |= TryMapOptional(snapshot.OwnerReference, personMap, value => updated = updated with { OwnerReference = value });
                    break;
            }

            return (changed, updated);
        });

        return new WorldLayerRemapResult
        {
            UpdatedCount = updatedCount,
            Warnings = warnings
        };
    }

    public WorldLayerRemapResult MergeAfterIdentityRegeneration(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld)
    {
        ArgumentNullException.ThrowIfNull(previousWorld);
        ArgumentNullException.ThrowIfNull(currentWorld);

        var warnings = new List<string>();
        var updatedCount = 0;

        var personMergeMap = BuildMergeMap(
            previousWorld.People,
            currentWorld.People,
            person => person.Id,
            BuildPersonKey,
            "person",
            warnings);
        if (personMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.People, person =>
            {
                var updated = person;
                var changed = false;
                changed |= TryMapOptional(person.ManagerPersonId, personMergeMap, value => updated = updated with { ManagerPersonId = value });
                changed |= TryMapOptional(person.SponsorPersonId, personMergeMap, value => updated = updated with { SponsorPersonId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.Accounts, account =>
            {
                var updated = account;
                var changed = TryMapOptional(account.PersonId, personMergeMap, value => updated = updated with { PersonId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.Devices, device =>
            {
                var updated = device;
                var changed = TryMapOptional(device.AssignedPersonId, personMergeMap, value => updated = updated with { AssignedPersonId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.TelephonyAssets, asset =>
            {
                var updated = asset;
                var changed = TryMapOptional(asset.AssignedPersonId, personMergeMap, value => updated = updated with { AssignedPersonId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.FileShares, share =>
            {
                var updated = share;
                var changed = TryMapOptional(share.OwnerPersonId, personMergeMap, value => updated = updated with { OwnerPersonId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.CollaborationSites, site =>
            {
                var updated = site;
                var changed = TryMapOptional(site.OwnerPersonId, personMergeMap, value => updated = updated with { OwnerPersonId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.SitePages, page =>
            {
                var updated = page;
                var changed = TryMapOptional(page.AuthorPersonId, personMergeMap, value => updated = updated with { AuthorPersonId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ObservedEntitySnapshots, snapshot =>
            {
                var updated = snapshot;
                var changed = snapshot.EntityType switch
                {
                    "FileShare" or "CollaborationSite" => TryMapOptional(snapshot.OwnerReference, personMergeMap, value => updated = updated with { OwnerReference = value }),
                    _ => false
                };

                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.People, person => person.Id, personMergeMap, "people", warnings);
        }

        var ouMergeMap = BuildMergeMap(
            previousWorld.OrganizationalUnits,
            currentWorld.OrganizationalUnits,
            ou => ou.Id,
            ou => $"{ou.CompanyId}|{Normalize(ou.DistinguishedName)}",
            "organizational unit",
            warnings);
        if (ouMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.OrganizationalUnits, ou =>
            {
                var updated = ou;
                var changed = TryMapOptional(ou.ParentOuId, ouMergeMap, value => updated = updated with { ParentOuId = value });
                return (changed, updated);
            });

            var currentOusById = currentWorld.OrganizationalUnits
                .Where(ou => !string.IsNullOrWhiteSpace(ou.Id))
                .ToDictionary(ou => ou.Id, StringComparer.OrdinalIgnoreCase);

            updatedCount += UpdateRecords(currentWorld.Accounts, account =>
            {
                var updated = account;
                var changed = TryMapOptional(account.OuId, ouMergeMap, value =>
                {
                    currentOusById.TryGetValue(value, out var targetOu);
                    updated = updated with
                    {
                        OuId = value,
                        DistinguishedName = targetOu is null ? updated.DistinguishedName : $"CN={ExtractCommonName(updated.DistinguishedName, updated.SamAccountName)},{targetOu.DistinguishedName}"
                    };
                });

                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.Groups, group =>
            {
                var updated = group;
                var changed = TryMapOptional(group.OuId, ouMergeMap, value =>
                {
                    currentOusById.TryGetValue(value, out var targetOu);
                    updated = updated with
                    {
                        OuId = value,
                        DistinguishedName = targetOu is null ? updated.DistinguishedName : $"CN={group.Name},{targetOu.DistinguishedName}"
                    };
                });

                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.Devices, device =>
            {
                var updated = device;
                var changed = TryMapOptional(device.OuId, ouMergeMap, value =>
                {
                    currentOusById.TryGetValue(value, out var targetOu);
                    updated = updated with
                    {
                        OuId = value,
                        DistinguishedName = targetOu is null ? updated.DistinguishedName : $"CN={device.Hostname},{targetOu.DistinguishedName}"
                    };
                });

                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.Servers, server =>
            {
                var updated = server;
                var changed = TryMapOptional(server.OuId, ouMergeMap, value =>
                {
                    currentOusById.TryGetValue(value, out var targetOu);
                    updated = updated with
                    {
                        OuId = value,
                        DistinguishedName = targetOu is null ? updated.DistinguishedName : $"CN={server.Hostname},{targetOu.DistinguishedName}"
                    };
                });

                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.OrganizationalUnits, ou => ou.Id, ouMergeMap, "organizational units", warnings);
        }

        var groupMergeMap = BuildMergeMap(
            previousWorld.Groups,
            currentWorld.Groups,
            group => group.Id,
            group => $"{group.CompanyId}|{Normalize(group.Name)}",
            "group",
            warnings);
        if (groupMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.GroupMemberships, membership =>
            {
                var updated = membership;
                var changed = TryMapOptional(membership.GroupId, groupMergeMap, value => updated = updated with { GroupId = value! });
                if (string.Equals(membership.MemberObjectType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= TryMapOptional(membership.MemberObjectId, groupMergeMap, value => updated = updated with { MemberObjectId = value! });
                }

                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
            {
                if (!string.Equals(grant.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, grant);
                }

                var updated = grant;
                var changed = TryMapOptional(grant.PrincipalObjectId, groupMergeMap, value => updated = updated with { PrincipalObjectId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointAdministrativeAssignments, assignment =>
            {
                if (!string.Equals(assignment.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, assignment);
                }

                var updated = assignment;
                var changed = TryMapOptional(assignment.PrincipalObjectId, groupMergeMap, value => updated = updated with { PrincipalObjectId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointLocalGroupMembers, membership =>
            {
                if (!string.Equals(membership.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.PrincipalObjectId, groupMergeMap, value => updated = updated with { PrincipalObjectId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.Groups, group => group.Id, groupMergeMap, "groups", warnings);
        }

        var accountMergeMap = BuildMergeMap(
            previousWorld.Accounts,
            currentWorld.Accounts,
            account => account.Id,
            account => BuildAccountKey(account, previousWorld.People, currentWorld.People),
            "account",
            warnings);
        if (accountMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.Accounts, account =>
            {
                var updated = account;
                var changed = false;
                changed |= TryMapOptional(account.ManagerAccountId, accountMergeMap, value => updated = updated with { ManagerAccountId = value });
                changed |= TryMapOptional(account.InvitedByAccountId, accountMergeMap, value => updated = updated with { InvitedByAccountId = value });
                changed |= TryMapOptional(account.PreviousInvitedByAccountId, accountMergeMap, value => updated = updated with { PreviousInvitedByAccountId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.GroupMemberships, membership =>
            {
                if (!string.Equals(membership.MemberObjectType, "Account", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.MemberObjectId, accountMergeMap, value => updated = updated with { MemberObjectId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.Devices, device =>
            {
                var updated = device;
                var changed = TryMapOptional(device.DirectoryAccountId, accountMergeMap, value => updated = updated with { DirectoryAccountId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
            {
                if (!string.Equals(grant.PrincipalType, "Account", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, grant);
                }

                var updated = grant;
                var changed = TryMapOptional(grant.PrincipalObjectId, accountMergeMap, value => updated = updated with { PrincipalObjectId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointAdministrativeAssignments, assignment =>
            {
                if (!string.Equals(assignment.PrincipalType, "Account", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, assignment);
                }

                var updated = assignment;
                var changed = TryMapOptional(assignment.PrincipalObjectId, accountMergeMap, value => updated = updated with { PrincipalObjectId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointLocalGroupMembers, membership =>
            {
                if (!string.Equals(membership.PrincipalType, "Account", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.PrincipalObjectId, accountMergeMap, value => updated = updated with { PrincipalObjectId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.CrossTenantAccessEvents, accessEvent =>
            {
                var updated = accessEvent;
                var changed = false;
                changed |= TryMapOptional(accessEvent.AccountId, accountMergeMap, value => updated = updated with { AccountId = value! });
                changed |= TryMapOptional(accessEvent.ActorAccountId, accountMergeMap, value => updated = updated with { ActorAccountId = value });
                return (changed, updated);
            });

            var currentAccountsById = currentWorld.Accounts
                .Where(account => !string.IsNullOrWhiteSpace(account.Id))
                .ToDictionary(account => account.Id, StringComparer.OrdinalIgnoreCase);

            updatedCount += UpdateRecords(currentWorld.ObservedEntitySnapshots, snapshot =>
            {
                var updated = snapshot;
                var changed = false;

                switch (snapshot.EntityType)
                {
                    case "Account":
                        changed |= TryMapOptional(snapshot.EntityId, accountMergeMap, value =>
                        {
                            updated = updated with
                            {
                                EntityId = value,
                                DisplayName = currentAccountsById.TryGetValue(value, out var account)
                                    ? account.UserPrincipalName
                                    : updated.DisplayName
                            };
                        });
                        changed |= TryMapOptional(snapshot.OwnerReference, accountMergeMap, value => updated = updated with { OwnerReference = value });
                        break;
                    case "Device":
                        changed |= TryMapOptional(snapshot.OwnerReference, accountMergeMap, value => updated = updated with { OwnerReference = value });
                        break;
                }

                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "Account", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, accountMergeMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.Accounts, account => account.Id, accountMergeMap, "accounts", warnings);
        }

        var policyMergeMap = BuildMergeMap(
            previousWorld.CrossTenantAccessPolicies,
            currentWorld.CrossTenantAccessPolicies,
            policy => policy.Id,
            policy => $"{policy.CompanyId}|{Normalize(policy.ExternalOrganizationId)}|{Normalize(policy.PolicyName)}|{Normalize(policy.AccessDirection)}|{Normalize(policy.RelationshipType)}",
            "cross-tenant access policy",
            warnings);
        if (policyMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.CrossTenantAccessEvents, accessEvent =>
            {
                var updated = accessEvent;
                var changed = TryMapOptional(accessEvent.PolicyId, policyMergeMap, value => updated = updated with { PolicyId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.CrossTenantAccessPolicies, policy => policy.Id, policyMergeMap, "cross-tenant access policies", warnings);
        }

        DeduplicateRecords(
            currentWorld.GroupMemberships,
            membership => $"{Normalize(membership.GroupId)}|{Normalize(membership.MemberObjectType)}|{Normalize(membership.MemberObjectId)}",
            "group memberships",
            warnings);
        DeduplicateRecords(
            currentWorld.RepositoryAccessGrants,
            grant => $"{Normalize(grant.RepositoryType)}|{Normalize(grant.RepositoryId)}|{Normalize(grant.PrincipalType)}|{Normalize(grant.PrincipalObjectId)}|{Normalize(grant.AccessLevel)}",
            "repository access grants",
            warnings);
        DeduplicateRecords(
            currentWorld.EndpointAdministrativeAssignments,
            assignment => $"{Normalize(assignment.EndpointType)}|{Normalize(assignment.EndpointId)}|{Normalize(assignment.PrincipalType)}|{Normalize(assignment.PrincipalObjectId)}|{Normalize(assignment.AccessRole)}",
            "endpoint administrative assignments",
            warnings);
        DeduplicateRecords(
            currentWorld.EndpointLocalGroupMembers,
            membership => $"{Normalize(membership.EndpointType)}|{Normalize(membership.EndpointId)}|{Normalize(membership.LocalGroupName)}|{Normalize(membership.PrincipalType)}|{Normalize(membership.PrincipalObjectId)}|{Normalize(membership.PrincipalName)}",
            "endpoint local-group memberships",
            warnings);
        DeduplicateRecords(
            currentWorld.CrossTenantAccessEvents,
            accessEvent => $"{Normalize(accessEvent.AccountId)}|{Normalize(accessEvent.ExternalOrganizationId)}|{Normalize(accessEvent.EventType)}|{Normalize(accessEvent.EventCategory)}",
            "cross-tenant access events",
            warnings);
        DeduplicateRecords(
            currentWorld.ObservedEntitySnapshots,
            snapshot => $"{Normalize(snapshot.EntityType)}|{Normalize(snapshot.EntityId)}|{Normalize(snapshot.SourceSystem)}|{Normalize(snapshot.DisplayName)}|{Normalize(snapshot.ObservedState)}|{Normalize(snapshot.GroundTruthState)}|{Normalize(snapshot.OwnerReference)}",
            "observed entity snapshots",
            warnings);

        if (updatedCount > 0)
        {
            warnings.Add($"Identity merge reconciled {updatedCount} identity binding(s) after regeneration.");
        }

        return new WorldLayerRemapResult
        {
            UpdatedCount = updatedCount,
            Warnings = warnings
        };
    }

    private static int PreserveIdentityStableIdentifiers(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld, List<string> warnings)
    {
        var updatedCount = 0;

        var personIdMap = InvertIdMap(BuildIdMap(
            previousWorld.People,
            currentWorld.People,
            person => person.Id,
            BuildPersonKey,
            "person",
            warnings));
        if (personIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.People, person =>
            {
                if (!personIdMap.TryGetValue(person.Id, out var preservedId))
                {
                    return (false, person);
                }

                return (true, person with { Id = preservedId });
            });

            updatedCount += UpdateRecords(currentWorld.Accounts, account =>
            {
                var updated = account;
                var changed = TryMapOptional(account.PersonId, personIdMap, value => updated = updated with { PersonId = value });
                return (changed, updated);
            });
        }

        var ouIdMap = InvertIdMap(BuildIdMap(
            previousWorld.OrganizationalUnits,
            currentWorld.OrganizationalUnits,
            ou => ou.Id,
            ou => $"{ou.CompanyId}|{Normalize(ou.DistinguishedName)}",
            "organizational unit",
            warnings));
        if (ouIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.OrganizationalUnits, ou =>
            {
                if (!ouIdMap.TryGetValue(ou.Id, out var preservedId))
                {
                    return (false, ou);
                }

                return (true, ou with { Id = preservedId });
            });

            updatedCount += UpdateRecords(currentWorld.OrganizationalUnits, ou =>
            {
                var updated = ou;
                var changed = TryMapOptional(ou.ParentOuId, ouIdMap, value => updated = updated with { ParentOuId = value });
                return (changed, updated);
            });

            var currentOusById = currentWorld.OrganizationalUnits
                .Where(ou => !string.IsNullOrWhiteSpace(ou.Id))
                .ToDictionary(ou => ou.Id, StringComparer.OrdinalIgnoreCase);

            updatedCount += UpdateRecords(currentWorld.Accounts, account =>
            {
                var updated = account;
                var changed = TryMapOptional(account.OuId, ouIdMap, value =>
                {
                    currentOusById.TryGetValue(value, out var targetOu);
                    updated = updated with
                    {
                        OuId = value,
                        DistinguishedName = targetOu is null ? updated.DistinguishedName : $"CN={ExtractCommonName(updated.DistinguishedName, updated.SamAccountName)},{targetOu.DistinguishedName}"
                    };
                });

                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.Groups, group =>
            {
                var updated = group;
                var changed = TryMapOptional(group.OuId, ouIdMap, value =>
                {
                    currentOusById.TryGetValue(value, out var targetOu);
                    updated = updated with
                    {
                        OuId = value,
                        DistinguishedName = targetOu is null ? updated.DistinguishedName : $"CN={group.Name},{targetOu.DistinguishedName}"
                    };
                });

                return (changed, updated);
            });
        }

        var groupIdMap = InvertIdMap(BuildIdMap(
            previousWorld.Groups,
            currentWorld.Groups,
            group => group.Id,
            group => $"{group.CompanyId}|{Normalize(group.Name)}",
            "group",
            warnings));
        if (groupIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.Groups, group =>
            {
                if (!groupIdMap.TryGetValue(group.Id, out var preservedId))
                {
                    return (false, group);
                }

                return (true, group with { Id = preservedId });
            });

            updatedCount += UpdateRecords(currentWorld.GroupMemberships, membership =>
            {
                var updated = membership;
                var changed = TryMapOptional(membership.GroupId, groupIdMap, value => updated = updated with { GroupId = value });
                if (string.Equals(membership.MemberObjectType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= TryMapOptional(membership.MemberObjectId, groupIdMap, value => updated = updated with { MemberObjectId = value });
                }

                return (changed, updated);
            });
        }

        var accountIdMap = InvertIdMap(BuildIdMap(
            previousWorld.Accounts,
            currentWorld.Accounts,
            account => account.Id,
            account => BuildAccountKey(account, previousWorld.People, currentWorld.People),
            "account",
            warnings));
        if (accountIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.Accounts, account =>
            {
                if (!accountIdMap.TryGetValue(account.Id, out var preservedId))
                {
                    return (false, account);
                }

                return (true, account with { Id = preservedId });
            });

            updatedCount += UpdateRecords(currentWorld.Accounts, account =>
            {
                var updated = account;
                var changed = false;
                changed |= TryMapOptional(account.ManagerAccountId, accountIdMap, value => updated = updated with { ManagerAccountId = value });
                changed |= TryMapOptional(account.InvitedByAccountId, accountIdMap, value => updated = updated with { InvitedByAccountId = value });
                changed |= TryMapOptional(account.PreviousInvitedByAccountId, accountIdMap, value => updated = updated with { PreviousInvitedByAccountId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.GroupMemberships, membership =>
            {
                if (!string.Equals(membership.MemberObjectType, "Account", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.MemberObjectId, accountIdMap, value => updated = updated with { MemberObjectId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.CrossTenantAccessEvents, accessEvent =>
            {
                var updated = accessEvent;
                var changed = false;
                changed |= TryMapOptional(accessEvent.AccountId, accountIdMap, value => updated = updated with { AccountId = value });
                changed |= TryMapOptional(accessEvent.ActorAccountId, accountIdMap, value => updated = updated with { ActorAccountId = value });
                return (changed, updated);
            });
        }

        if (updatedCount > 0)
        {
            warnings.Add($"Identity replacement preserved {updatedCount} stable identifier or reference binding(s) before downstream remap.");
        }

        return updatedCount;
    }

    public WorldLayerRemapResult RemapAfterInfrastructureReplacement(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld)
    {
        ArgumentNullException.ThrowIfNull(previousWorld);
        ArgumentNullException.ThrowIfNull(currentWorld);

        var warnings = new List<string>();
        var updatedCount = PreserveInfrastructureStableIdentifiers(previousWorld, currentWorld, warnings);

        var serverMap = BuildIdMap(
            previousWorld.Servers,
            currentWorld.Servers,
            server => server.Id,
            BuildServerKey,
            "server",
            warnings);
        var deviceMap = BuildIdMap(
            previousWorld.Devices,
            currentWorld.Devices,
            device => device.Id,
            BuildDeviceKey,
            "device",
            warnings);
        var currentServersById = currentWorld.Servers
            .Where(server => !string.IsNullOrWhiteSpace(server.Id))
            .ToDictionary(server => server.Id, StringComparer.OrdinalIgnoreCase);
        var currentDevicesById = currentWorld.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .ToDictionary(device => device.Id, StringComparer.OrdinalIgnoreCase);

        updatedCount += UpdateRecords(currentWorld.ApplicationServiceHostings, hosting =>
        {
            var updated = hosting;
            var changed = false;

            if (string.Equals(hosting.HostType, "Server", StringComparison.OrdinalIgnoreCase))
            {
                changed |= TryMapOptional(hosting.HostId, serverMap, value =>
                {
                    updated = updated with
                    {
                        HostId = value,
                        HostName = currentServersById.TryGetValue(value, out var server) ? server.Hostname : updated.HostName
                    };
                });
            }
            else if (string.Equals(hosting.HostType, "Device", StringComparison.OrdinalIgnoreCase))
            {
                changed |= TryMapOptional(hosting.HostId, deviceMap, value =>
                {
                    updated = updated with
                    {
                        HostId = value,
                        HostName = currentDevicesById.TryGetValue(value, out var device) ? device.Hostname : updated.HostName
                    };
                });
            }

            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.Databases, database =>
        {
            var updated = database;
            var changed = TryMapOptional(database.HostServerId, serverMap, value => updated = updated with { HostServerId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.FileShares, share =>
        {
            var updated = share;
            var changed = TryMapOptional(share.HostServerId, serverMap, value => updated = updated with { HostServerId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.DeviceSoftwareInstallations, installation =>
        {
            var updated = installation;
            var changed = TryMapRequired(installation.DeviceId, deviceMap, value => updated = updated with { DeviceId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.ServerSoftwareInstallations, installation =>
        {
            var updated = installation;
            var changed = TryMapRequired(installation.ServerId, serverMap, value => updated = updated with { ServerId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.GroupMemberships, membership =>
        {
            var updated = membership;
            var changed = string.Equals(membership.MemberObjectType, "Device", StringComparison.OrdinalIgnoreCase)
                ? TryMapRequired(membership.MemberObjectId, deviceMap, value => updated = updated with { MemberObjectId = value })
                : string.Equals(membership.MemberObjectType, "Server", StringComparison.OrdinalIgnoreCase)
                    ? TryMapRequired(membership.MemberObjectId, serverMap, value => updated = updated with { MemberObjectId = value })
                    : false;
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.EndpointAdministrativeAssignments, assignment =>
        {
            var updated = assignment;
            var changed = string.Equals(assignment.EndpointType, "Server", StringComparison.OrdinalIgnoreCase)
                ? TryMapRequired(assignment.EndpointId, serverMap, value => updated = updated with { EndpointId = value })
                : TryMapRequired(assignment.EndpointId, deviceMap, value => updated = updated with { EndpointId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.EndpointPolicyBaselines, baseline =>
        {
            var updated = baseline;
            var changed = string.Equals(baseline.EndpointType, "Server", StringComparison.OrdinalIgnoreCase)
                ? TryMapRequired(baseline.EndpointId, serverMap, value => updated = updated with { EndpointId = value })
                : TryMapRequired(baseline.EndpointId, deviceMap, value => updated = updated with { EndpointId = value });
            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.EndpointLocalGroupMembers, membership =>
        {
            var updated = membership;
            var changed = string.Equals(membership.EndpointType, "Server", StringComparison.OrdinalIgnoreCase)
                ? TryMapRequired(membership.EndpointId, serverMap, value => updated = updated with { EndpointId = value })
                : TryMapRequired(membership.EndpointId, deviceMap, value => updated = updated with { EndpointId = value });
            return (changed, updated);
        });

        DeduplicateRecords(
            currentWorld.GroupMemberships,
            membership => $"{Normalize(membership.GroupId)}|{Normalize(membership.MemberObjectType)}|{Normalize(membership.MemberObjectId)}",
            "group memberships",
            warnings);
        DeduplicateRecords(
            currentWorld.EndpointAdministrativeAssignments,
            assignment => $"{Normalize(assignment.EndpointType)}|{Normalize(assignment.EndpointId)}|{Normalize(assignment.PrincipalType)}|{Normalize(assignment.PrincipalObjectId)}|{Normalize(assignment.AccessRole)}",
            "endpoint administrative assignments",
            warnings);
        DeduplicateRecords(
            currentWorld.EndpointPolicyBaselines,
            baseline => $"{Normalize(baseline.EndpointType)}|{Normalize(baseline.EndpointId)}|{Normalize(baseline.PolicyName)}|{Normalize(baseline.AssignedFrom)}",
            "endpoint policy baselines",
            warnings);
        DeduplicateRecords(
            currentWorld.EndpointLocalGroupMembers,
            membership => $"{Normalize(membership.EndpointType)}|{Normalize(membership.EndpointId)}|{Normalize(membership.LocalGroupName)}|{Normalize(membership.PrincipalType)}|{Normalize(membership.PrincipalObjectId)}|{Normalize(membership.PrincipalName)}",
            "endpoint local-group memberships",
            warnings);

        updatedCount += UpdateRecords(currentWorld.ObservedEntitySnapshots, snapshot =>
        {
            var updated = snapshot;
            var changed = false;

            switch (snapshot.EntityType)
            {
                case "Server":
                    changed |= TryMapRequired(snapshot.EntityId, serverMap, value =>
                    {
                        updated = updated with
                        {
                            EntityId = value,
                            DisplayName = currentServersById.TryGetValue(value, out var server) ? server.Hostname : updated.DisplayName
                        };
                    });
                    break;
                case "Device":
                    changed |= TryMapRequired(snapshot.EntityId, deviceMap, value =>
                    {
                        updated = updated with
                        {
                            EntityId = value,
                            DisplayName = currentDevicesById.TryGetValue(value, out var device) ? device.Hostname : updated.DisplayName
                        };
                    });
                    break;
            }

            return (changed, updated);
        });

        return new WorldLayerRemapResult
        {
            UpdatedCount = updatedCount,
            Warnings = warnings
        };
    }

    public WorldLayerRemapResult MergeAfterInfrastructureRegeneration(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld)
    {
        ArgumentNullException.ThrowIfNull(previousWorld);
        ArgumentNullException.ThrowIfNull(currentWorld);

        var warnings = new List<string>();
        var updatedCount = 0;

        var softwareMergeMap = BuildMergeMap(
            previousWorld.SoftwarePackages,
            currentWorld.SoftwarePackages,
            package => package.Id,
            BuildSoftwarePackageKey,
            "software package",
            warnings);
        if (softwareMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.DeviceSoftwareInstallations, installation =>
            {
                var updated = installation;
                var changed = TryMapOptional(installation.SoftwareId, softwareMergeMap, value => updated = updated with { SoftwareId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ServerSoftwareInstallations, installation =>
            {
                var updated = installation;
                var changed = TryMapOptional(installation.SoftwareId, softwareMergeMap, value => updated = updated with { SoftwareId = value! });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.SoftwarePackages, package => package.Id, softwareMergeMap, "software packages", warnings);
        }

        var serverMergeMap = BuildMergeMap(
            previousWorld.Servers,
            currentWorld.Servers,
            server => server.Id,
            BuildServerKey,
            "server",
            warnings);
        if (serverMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.ApplicationServiceHostings, hosting =>
            {
                if (!string.Equals(hosting.HostType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, hosting);
                }

                var updated = hosting;
                var changed = TryMapOptional(hosting.HostId, serverMergeMap, value => updated = updated with { HostId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.Databases, database =>
            {
                var updated = database;
                var changed = TryMapOptional(database.HostServerId, serverMergeMap, value => updated = updated with { HostServerId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.FileShares, share =>
            {
                var updated = share;
                var changed = TryMapOptional(share.HostServerId, serverMergeMap, value => updated = updated with { HostServerId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.GroupMemberships, membership =>
            {
                if (!string.Equals(membership.MemberObjectType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.MemberObjectId, serverMergeMap, value => updated = updated with { MemberObjectId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointAdministrativeAssignments, assignment =>
            {
                if (!string.Equals(assignment.EndpointType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, assignment);
                }

                var updated = assignment;
                var changed = TryMapOptional(assignment.EndpointId, serverMergeMap, value => updated = updated with { EndpointId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointPolicyBaselines, baseline =>
            {
                if (!string.Equals(baseline.EndpointType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, baseline);
                }

                var updated = baseline;
                var changed = TryMapOptional(baseline.EndpointId, serverMergeMap, value => updated = updated with { EndpointId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointLocalGroupMembers, membership =>
            {
                if (!string.Equals(membership.EndpointType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.EndpointId, serverMergeMap, value => updated = updated with { EndpointId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ServerSoftwareInstallations, installation =>
            {
                var updated = installation;
                var changed = TryMapOptional(installation.ServerId, serverMergeMap, value => updated = updated with { ServerId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ObservedEntitySnapshots, snapshot =>
            {
                if (!string.Equals(snapshot.EntityType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, snapshot);
                }

                var updated = snapshot;
                var changed = TryMapOptional(snapshot.EntityId, serverMergeMap, value => updated = updated with { EntityId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, serverMergeMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.Servers, server => server.Id, serverMergeMap, "servers", warnings);
        }

        var deviceMergeMap = BuildMergeMap(
            previousWorld.Devices,
            currentWorld.Devices,
            device => device.Id,
            BuildDeviceKey,
            "device",
            warnings);
        if (deviceMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.GroupMemberships, membership =>
            {
                if (!string.Equals(membership.MemberObjectType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.MemberObjectId, deviceMergeMap, value => updated = updated with { MemberObjectId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointAdministrativeAssignments, assignment =>
            {
                if (!string.Equals(assignment.EndpointType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, assignment);
                }

                var updated = assignment;
                var changed = TryMapOptional(assignment.EndpointId, deviceMergeMap, value => updated = updated with { EndpointId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointPolicyBaselines, baseline =>
            {
                if (!string.Equals(baseline.EndpointType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, baseline);
                }

                var updated = baseline;
                var changed = TryMapOptional(baseline.EndpointId, deviceMergeMap, value => updated = updated with { EndpointId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointLocalGroupMembers, membership =>
            {
                if (!string.Equals(membership.EndpointType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.EndpointId, deviceMergeMap, value => updated = updated with { EndpointId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.DeviceSoftwareInstallations, installation =>
            {
                var updated = installation;
                var changed = TryMapOptional(installation.DeviceId, deviceMergeMap, value => updated = updated with { DeviceId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ObservedEntitySnapshots, snapshot =>
            {
                if (!string.Equals(snapshot.EntityType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, snapshot);
                }

                var updated = snapshot;
                var changed = TryMapOptional(snapshot.EntityId, deviceMergeMap, value => updated = updated with { EntityId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, deviceMergeMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.Devices, device => device.Id, deviceMergeMap, "devices", warnings);
        }

        var networkMergeMap = BuildMergeMap(
            previousWorld.NetworkAssets,
            currentWorld.NetworkAssets,
            asset => asset.Id,
            BuildNetworkAssetKey,
            "network asset",
            warnings);
        if (networkMergeMap.Count > 0)
        {
            RemoveMappedDuplicates(currentWorld.NetworkAssets, asset => asset.Id, networkMergeMap, "network assets", warnings);
        }

        var telephonyMergeMap = BuildMergeMap(
            previousWorld.TelephonyAssets,
            currentWorld.TelephonyAssets,
            asset => asset.Id,
            BuildTelephonyAssetKey,
            "telephony asset",
            warnings);
        if (telephonyMergeMap.Count > 0)
        {
            RemoveMappedDuplicates(currentWorld.TelephonyAssets, asset => asset.Id, telephonyMergeMap, "telephony assets", warnings);
        }

        DeduplicateRecords(
            currentWorld.GroupMemberships,
            membership => $"{Normalize(membership.GroupId)}|{Normalize(membership.MemberObjectType)}|{Normalize(membership.MemberObjectId)}",
            "group memberships",
            warnings);
        DeduplicateRecords(
            currentWorld.EndpointAdministrativeAssignments,
            assignment => $"{Normalize(assignment.EndpointType)}|{Normalize(assignment.EndpointId)}|{Normalize(assignment.PrincipalType)}|{Normalize(assignment.PrincipalObjectId)}|{Normalize(assignment.AccessRole)}",
            "endpoint administrative assignments",
            warnings);
        DeduplicateRecords(
            currentWorld.EndpointPolicyBaselines,
            baseline => $"{Normalize(baseline.EndpointType)}|{Normalize(baseline.EndpointId)}|{Normalize(baseline.PolicyName)}|{Normalize(baseline.PolicyCategory)}",
            "endpoint policy baselines",
            warnings);
        DeduplicateRecords(
            currentWorld.EndpointLocalGroupMembers,
            membership => $"{Normalize(membership.EndpointType)}|{Normalize(membership.EndpointId)}|{Normalize(membership.LocalGroupName)}|{Normalize(membership.PrincipalType)}|{Normalize(membership.PrincipalObjectId)}|{Normalize(membership.PrincipalName)}",
            "endpoint local-group memberships",
            warnings);
        DeduplicateRecords(
            currentWorld.DeviceSoftwareInstallations,
            installation => $"{Normalize(installation.DeviceId)}|{Normalize(installation.SoftwareId)}",
            "device software installations",
            warnings);
        DeduplicateRecords(
            currentWorld.ServerSoftwareInstallations,
            installation => $"{Normalize(installation.ServerId)}|{Normalize(installation.SoftwareId)}",
            "server software installations",
            warnings);
        DeduplicateRecords(
            currentWorld.ObservedEntitySnapshots,
            snapshot => $"{Normalize(snapshot.EntityType)}|{Normalize(snapshot.EntityId)}|{Normalize(snapshot.SourceSystem)}|{Normalize(snapshot.DisplayName)}|{Normalize(snapshot.ObservedState)}|{Normalize(snapshot.GroundTruthState)}|{Normalize(snapshot.OwnerReference)}",
            "observed entity snapshots",
            warnings);

        warnings.Add($"Infrastructure merge reconciled {updatedCount} infrastructure binding(s) after regeneration.");

        return new WorldLayerRemapResult
        {
            UpdatedCount = updatedCount,
            Warnings = warnings
        };
    }

    private static int PreserveInfrastructureStableIdentifiers(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld, List<string> warnings)
    {
        var updatedCount = 0;

        var serverIdMap = InvertIdMap(BuildIdMap(
            previousWorld.Servers,
            currentWorld.Servers,
            server => server.Id,
            BuildServerKey,
            "server",
            warnings));
        if (serverIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.Servers, server =>
            {
                if (!serverIdMap.TryGetValue(server.Id, out var preservedId))
                {
                    return (false, server);
                }

                return (true, server with { Id = preservedId });
            });

            updatedCount += UpdateRecords(currentWorld.ApplicationServiceHostings, hosting =>
            {
                if (!string.Equals(hosting.HostType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, hosting);
                }

                var updated = hosting;
                var changed = TryMapOptional(hosting.HostId, serverIdMap, value => updated = updated with { HostId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.Databases, database =>
            {
                var updated = database;
                var changed = TryMapOptional(database.HostServerId, serverIdMap, value => updated = updated with { HostServerId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.FileShares, share =>
            {
                var updated = share;
                var changed = TryMapOptional(share.HostServerId, serverIdMap, value => updated = updated with { HostServerId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ServerSoftwareInstallations, installation =>
            {
                var updated = installation;
                var changed = TryMapOptional(installation.ServerId, serverIdMap, value => updated = updated with { ServerId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.GroupMemberships, membership =>
            {
                if (!string.Equals(membership.MemberObjectType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.MemberObjectId, serverIdMap, value => updated = updated with { MemberObjectId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointAdministrativeAssignments, assignment =>
            {
                if (!string.Equals(assignment.EndpointType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, assignment);
                }

                var updated = assignment;
                var changed = TryMapOptional(assignment.EndpointId, serverIdMap, value => updated = updated with { EndpointId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointPolicyBaselines, baseline =>
            {
                if (!string.Equals(baseline.EndpointType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, baseline);
                }

                var updated = baseline;
                var changed = TryMapOptional(baseline.EndpointId, serverIdMap, value => updated = updated with { EndpointId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointLocalGroupMembers, membership =>
            {
                if (!string.Equals(membership.EndpointType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.EndpointId, serverIdMap, value => updated = updated with { EndpointId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ObservedEntitySnapshots, snapshot =>
            {
                if (!string.Equals(snapshot.EntityType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, snapshot);
                }

                var updated = snapshot;
                var changed = TryMapOptional(snapshot.EntityId, serverIdMap, value => updated = updated with { EntityId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "Server", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, serverIdMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });
        }

        var deviceIdMap = InvertIdMap(BuildIdMap(
            previousWorld.Devices,
            currentWorld.Devices,
            device => device.Id,
            BuildDeviceKey,
            "device",
            warnings));
        if (deviceIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.Devices, device =>
            {
                if (!deviceIdMap.TryGetValue(device.Id, out var preservedId))
                {
                    return (false, device);
                }

                return (true, device with { Id = preservedId });
            });

            updatedCount += UpdateRecords(currentWorld.ApplicationServiceHostings, hosting =>
            {
                if (!string.Equals(hosting.HostType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, hosting);
                }

                var updated = hosting;
                var changed = TryMapOptional(hosting.HostId, deviceIdMap, value => updated = updated with { HostId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.DeviceSoftwareInstallations, installation =>
            {
                var updated = installation;
                var changed = TryMapOptional(installation.DeviceId, deviceIdMap, value => updated = updated with { DeviceId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.GroupMemberships, membership =>
            {
                if (!string.Equals(membership.MemberObjectType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.MemberObjectId, deviceIdMap, value => updated = updated with { MemberObjectId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointAdministrativeAssignments, assignment =>
            {
                if (!string.Equals(assignment.EndpointType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, assignment);
                }

                var updated = assignment;
                var changed = TryMapOptional(assignment.EndpointId, deviceIdMap, value => updated = updated with { EndpointId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointPolicyBaselines, baseline =>
            {
                if (!string.Equals(baseline.EndpointType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, baseline);
                }

                var updated = baseline;
                var changed = TryMapOptional(baseline.EndpointId, deviceIdMap, value => updated = updated with { EndpointId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.EndpointLocalGroupMembers, membership =>
            {
                if (!string.Equals(membership.EndpointType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, membership);
                }

                var updated = membership;
                var changed = TryMapOptional(membership.EndpointId, deviceIdMap, value => updated = updated with { EndpointId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ObservedEntitySnapshots, snapshot =>
            {
                if (!string.Equals(snapshot.EntityType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, snapshot);
                }

                var updated = snapshot;
                var changed = TryMapOptional(snapshot.EntityId, deviceIdMap, value => updated = updated with { EntityId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "Device", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, deviceIdMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });
        }

        if (updatedCount > 0)
        {
            warnings.Add($"Infrastructure replacement preserved {updatedCount} stable identifier or reference binding(s) before downstream remap.");
        }

        return updatedCount;
    }

    public WorldLayerRemapResult RemapAfterRepositoryReplacement(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld)
    {
        ArgumentNullException.ThrowIfNull(previousWorld);
        ArgumentNullException.ThrowIfNull(currentWorld);

        var warnings = new List<string>();
        var updatedCount = PreserveRepositoryStableIdentifiers(previousWorld, currentWorld, warnings);

        var databaseMap = BuildIdMap(
            previousWorld.Databases,
            currentWorld.Databases,
            database => database.Id,
            database => BuildDatabaseKey(database),
            "database",
            warnings);
        var fileShareMap = BuildIdMap(
            previousWorld.FileShares,
            currentWorld.FileShares,
            share => share.Id,
            share => BuildFileShareKey(share),
            "file share",
            warnings);
        var siteMap = BuildIdMap(
            previousWorld.CollaborationSites,
            currentWorld.CollaborationSites,
            site => site.Id,
            site => BuildCollaborationSiteKey(site),
            "collaboration site",
            warnings);
        var channelMap = BuildIdMap(
            previousWorld.CollaborationChannels,
            currentWorld.CollaborationChannels,
            channel => channel.Id,
            channel => BuildCollaborationChannelKey(
                channel,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "collaboration channel",
            warnings);
        var libraryMap = BuildIdMap(
            previousWorld.DocumentLibraries,
            currentWorld.DocumentLibraries,
            library => library.Id,
            library => BuildDocumentLibraryKey(
                library,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "document library",
            warnings);
        var pageMap = BuildIdMap(
            previousWorld.SitePages,
            currentWorld.SitePages,
            page => page.Id,
            page => BuildSitePageKey(
                page,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "site page",
            warnings);
        var folderMap = BuildFolderMap(previousWorld, currentWorld, warnings);

        var currentFileSharesById = currentWorld.FileShares
            .Where(share => !string.IsNullOrWhiteSpace(share.Id))
            .ToDictionary(share => share.Id, StringComparer.OrdinalIgnoreCase);
        var currentSitesById = currentWorld.CollaborationSites
            .Where(site => !string.IsNullOrWhiteSpace(site.Id))
            .ToDictionary(site => site.Id, StringComparer.OrdinalIgnoreCase);
        var currentLibrariesById = currentWorld.DocumentLibraries
            .Where(library => !string.IsNullOrWhiteSpace(library.Id))
            .ToDictionary(library => library.Id, StringComparer.OrdinalIgnoreCase);
        var currentPagesById = currentWorld.SitePages
            .Where(page => !string.IsNullOrWhiteSpace(page.Id))
            .ToDictionary(page => page.Id, StringComparer.OrdinalIgnoreCase);

        updatedCount += UpdateRecords(currentWorld.ObservedEntitySnapshots, snapshot =>
        {
            var updated = snapshot;
            var changed = snapshot.EntityType switch
            {
                "FileShare" => TryMapRequired(snapshot.EntityId, fileShareMap, value =>
                {
                    updated = updated with
                    {
                        EntityId = value,
                        DisplayName = currentFileSharesById.TryGetValue(value, out var share)
                            ? share.UncPath
                            : updated.DisplayName
                    };
                }),
                "CollaborationSite" => TryMapRequired(snapshot.EntityId, siteMap, value =>
                {
                    updated = updated with
                    {
                        EntityId = value,
                        DisplayName = currentSitesById.TryGetValue(value, out var site)
                            ? site.Name
                            : updated.DisplayName
                    };
                }),
                "DocumentLibrary" => TryMapRequired(snapshot.EntityId, libraryMap, value =>
                {
                    updated = updated with
                    {
                        EntityId = value,
                        DisplayName = currentLibrariesById.TryGetValue(value, out var library)
                            ? library.Name
                            : updated.DisplayName
                    };
                }),
                "SitePage" => TryMapRequired(snapshot.EntityId, pageMap, value =>
                {
                    updated = updated with
                    {
                        EntityId = value,
                        DisplayName = currentPagesById.TryGetValue(value, out var page)
                            ? page.Title
                            : updated.DisplayName
                    };
                }),
                _ => false
            };

            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
        {
            var updated = record;
            var changed = record.AssociatedEntityType switch
            {
                "Database" => TryMapOptional(record.AssociatedEntityId, databaseMap, value => updated = updated with { AssociatedEntityId = value }),
                "FileShare" => TryMapOptional(record.AssociatedEntityId, fileShareMap, value => updated = updated with { AssociatedEntityId = value }),
                "CollaborationSite" => TryMapOptional(record.AssociatedEntityId, siteMap, value => updated = updated with { AssociatedEntityId = value }),
                "CollaborationChannel" => TryMapOptional(record.AssociatedEntityId, channelMap, value => updated = updated with { AssociatedEntityId = value }),
                "DocumentLibrary" => TryMapOptional(record.AssociatedEntityId, libraryMap, value => updated = updated with { AssociatedEntityId = value }),
                "SitePage" => TryMapOptional(record.AssociatedEntityId, pageMap, value => updated = updated with { AssociatedEntityId = value }),
                "DocumentFolder" => TryMapOptional(record.AssociatedEntityId, folderMap, value => updated = updated with { AssociatedEntityId = value }),
                _ => false
            };

            return (changed, updated);
        });

        return new WorldLayerRemapResult
        {
            UpdatedCount = updatedCount,
            Warnings = warnings
        };
    }

    public WorldLayerRemapResult MergeAfterRepositoryRegeneration(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld)
    {
        ArgumentNullException.ThrowIfNull(previousWorld);
        ArgumentNullException.ThrowIfNull(currentWorld);

        var warnings = new List<string>();
        var updatedCount = 0;

        var databaseMergeMap = BuildMergeMap(
            previousWorld.Databases,
            currentWorld.Databases,
            database => database.Id,
            BuildDatabaseKey,
            "database",
            warnings);
        if (databaseMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
            {
                if (!string.Equals(grant.RepositoryType, "Database", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, grant);
                }

                var updated = grant;
                var changed = TryMapOptional(grant.RepositoryId, databaseMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ApplicationRepositoryLinks, link =>
            {
                if (!string.Equals(link.RepositoryType, "Database", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, link);
                }

                var updated = link;
                var changed = TryMapOptional(link.RepositoryId, databaseMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "Database", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, databaseMergeMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.Databases, database => database.Id, databaseMergeMap, "databases", warnings);
        }

        var fileShareMergeMap = BuildMergeMap(
            previousWorld.FileShares,
            currentWorld.FileShares,
            share => share.Id,
            BuildFileShareKey,
            "file share",
            warnings);
        if (fileShareMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
            {
                if (!string.Equals(grant.RepositoryType, "FileShare", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, grant);
                }

                var updated = grant;
                var changed = TryMapOptional(grant.RepositoryId, fileShareMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ApplicationRepositoryLinks, link =>
            {
                if (!string.Equals(link.RepositoryType, "FileShare", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, link);
                }

                var updated = link;
                var changed = TryMapOptional(link.RepositoryId, fileShareMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "FileShare", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, fileShareMergeMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.FileShares, share => share.Id, fileShareMergeMap, "file shares", warnings);
        }

        var siteMergeMap = BuildMergeMap(
            previousWorld.CollaborationSites,
            currentWorld.CollaborationSites,
            site => site.Id,
            BuildCollaborationSiteKey,
            "collaboration site",
            warnings);
        if (siteMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.CollaborationChannels, channel =>
            {
                var updated = channel;
                var changed = TryMapOptional(channel.CollaborationSiteId, siteMergeMap, value => updated = updated with { CollaborationSiteId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.DocumentLibraries, library =>
            {
                var updated = library;
                var changed = TryMapOptional(library.CollaborationSiteId, siteMergeMap, value => updated = updated with { CollaborationSiteId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.SitePages, page =>
            {
                var updated = page;
                var changed = TryMapOptional(page.CollaborationSiteId, siteMergeMap, value => updated = updated with { CollaborationSiteId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
            {
                if (!string.Equals(grant.RepositoryType, "CollaborationSite", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, grant);
                }

                var updated = grant;
                var changed = TryMapOptional(grant.RepositoryId, siteMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ApplicationRepositoryLinks, link =>
            {
                if (!string.Equals(link.RepositoryType, "CollaborationSite", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, link);
                }

                var updated = link;
                var changed = TryMapOptional(link.RepositoryId, siteMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.CollaborationChannelTabs, tab =>
            {
                if (!string.Equals(tab.TargetType, "CollaborationSite", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, tab);
                }

                var updated = tab;
                var changed = TryMapOptional(tab.TargetId, siteMergeMap, value => updated = updated with { TargetId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "CollaborationSite", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, siteMergeMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.CollaborationSites, site => site.Id, siteMergeMap, "collaboration sites", warnings);
        }

        var channelMergeMap = BuildMergeMap(
            previousWorld.CollaborationChannels,
            currentWorld.CollaborationChannels,
            channel => channel.Id,
            channel => BuildCollaborationChannelKey(
                channel,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "collaboration channel",
            warnings);
        if (channelMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.CollaborationChannelTabs, tab =>
            {
                var updated = tab;
                var changed = TryMapOptional(tab.CollaborationChannelId, channelMergeMap, value => updated = updated with { CollaborationChannelId = value! });
                if (string.Equals(tab.TargetType, "CollaborationChannel", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= TryMapOptional(tab.TargetId, channelMergeMap, value => updated = updated with { TargetId = value });
                }

                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
            {
                if (!string.Equals(grant.RepositoryType, "CollaborationChannel", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, grant);
                }

                var updated = grant;
                var changed = TryMapOptional(grant.RepositoryId, channelMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ApplicationRepositoryLinks, link =>
            {
                if (!string.Equals(link.RepositoryType, "CollaborationChannel", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, link);
                }

                var updated = link;
                var changed = TryMapOptional(link.RepositoryId, channelMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "CollaborationChannel", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, channelMergeMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.CollaborationChannels, channel => channel.Id, channelMergeMap, "collaboration channels", warnings);
        }

        var libraryMergeMap = BuildMergeMap(
            previousWorld.DocumentLibraries,
            currentWorld.DocumentLibraries,
            library => library.Id,
            library => BuildDocumentLibraryKey(
                library,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "document library",
            warnings);
        if (libraryMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.SitePages, page =>
            {
                var updated = page;
                var changed = TryMapOptional(page.AssociatedLibraryId, libraryMergeMap, value => updated = updated with { AssociatedLibraryId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.DocumentFolders, folder =>
            {
                var updated = folder;
                var changed = TryMapOptional(folder.DocumentLibraryId, libraryMergeMap, value => updated = updated with { DocumentLibraryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.CollaborationChannelTabs, tab =>
            {
                if (!string.Equals(tab.TargetType, "DocumentLibrary", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, tab);
                }

                var updated = tab;
                var changed = TryMapOptional(tab.TargetId, libraryMergeMap, value => updated = updated with { TargetId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
            {
                if (!string.Equals(grant.RepositoryType, "DocumentLibrary", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, grant);
                }

                var updated = grant;
                var changed = TryMapOptional(grant.RepositoryId, libraryMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ApplicationRepositoryLinks, link =>
            {
                if (!string.Equals(link.RepositoryType, "DocumentLibrary", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, link);
                }

                var updated = link;
                var changed = TryMapOptional(link.RepositoryId, libraryMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "DocumentLibrary", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, libraryMergeMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.DocumentLibraries, library => library.Id, libraryMergeMap, "document libraries", warnings);
        }

        var pageMergeMap = BuildMergeMap(
            previousWorld.SitePages,
            currentWorld.SitePages,
            page => page.Id,
            page => BuildSitePageKey(
                page,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "site page",
            warnings);
        if (pageMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
            {
                if (!string.Equals(grant.RepositoryType, "SitePage", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, grant);
                }

                var updated = grant;
                var changed = TryMapOptional(grant.RepositoryId, pageMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ApplicationRepositoryLinks, link =>
            {
                if (!string.Equals(link.RepositoryType, "SitePage", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, link);
                }

                var updated = link;
                var changed = TryMapOptional(link.RepositoryId, pageMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "SitePage", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, pageMergeMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.SitePages, page => page.Id, pageMergeMap, "site pages", warnings);
        }

        var previousFoldersById = previousWorld.DocumentFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Id))
            .ToDictionary(folder => folder.Id, StringComparer.OrdinalIgnoreCase);
        var currentFoldersById = currentWorld.DocumentFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Id))
            .ToDictionary(folder => folder.Id, StringComparer.OrdinalIgnoreCase);

        var folderMergeMap = BuildMergeMap(
            previousWorld.DocumentFolders,
            currentWorld.DocumentFolders,
            folder => folder.Id,
            folder => BuildDocumentFolderKey(
                folder,
                previousFoldersById,
                currentFoldersById,
                previousWorld.DocumentLibraries,
                currentWorld.DocumentLibraries,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "document folder",
            warnings);
        if (folderMergeMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.DocumentFolders, folder =>
            {
                var updated = folder;
                var changed = TryMapOptional(folder.ParentFolderId, folderMergeMap, value => updated = updated with { ParentFolderId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
            {
                if (!string.Equals(grant.RepositoryType, "DocumentFolder", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, grant);
                }

                var updated = grant;
                var changed = TryMapOptional(grant.RepositoryId, folderMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.ApplicationRepositoryLinks, link =>
            {
                if (!string.Equals(link.RepositoryType, "DocumentFolder", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, link);
                }

                var updated = link;
                var changed = TryMapOptional(link.RepositoryId, folderMergeMap, value => updated = updated with { RepositoryId = value! });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.PluginRecords, record =>
            {
                if (!string.Equals(record.AssociatedEntityType, "DocumentFolder", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, record);
                }

                var updated = record;
                var changed = TryMapOptional(record.AssociatedEntityId, folderMergeMap, value => updated = updated with { AssociatedEntityId = value });
                return (changed, updated);
            });

            RemoveMappedDuplicates(currentWorld.DocumentFolders, folder => folder.Id, folderMergeMap, "document folders", warnings);
        }

        var currentFileSharesById = currentWorld.FileShares
            .Where(share => !string.IsNullOrWhiteSpace(share.Id))
            .ToDictionary(share => share.Id, StringComparer.OrdinalIgnoreCase);
        var currentSitesById = currentWorld.CollaborationSites
            .Where(site => !string.IsNullOrWhiteSpace(site.Id))
            .ToDictionary(site => site.Id, StringComparer.OrdinalIgnoreCase);
        var currentLibrariesById = currentWorld.DocumentLibraries
            .Where(library => !string.IsNullOrWhiteSpace(library.Id))
            .ToDictionary(library => library.Id, StringComparer.OrdinalIgnoreCase);
        var currentPagesById = currentWorld.SitePages
            .Where(page => !string.IsNullOrWhiteSpace(page.Id))
            .ToDictionary(page => page.Id, StringComparer.OrdinalIgnoreCase);

        updatedCount += UpdateRecords(currentWorld.ObservedEntitySnapshots, snapshot =>
        {
            var updated = snapshot;
            var changed = snapshot.EntityType switch
            {
                "FileShare" => TryMapOptional(snapshot.EntityId, fileShareMergeMap, value =>
                {
                    updated = updated with
                    {
                        EntityId = value,
                        DisplayName = currentFileSharesById.TryGetValue(value, out var share)
                            ? share.UncPath
                            : updated.DisplayName
                    };
                }),
                "CollaborationSite" => TryMapOptional(snapshot.EntityId, siteMergeMap, value =>
                {
                    updated = updated with
                    {
                        EntityId = value,
                        DisplayName = currentSitesById.TryGetValue(value, out var site)
                            ? site.Name
                            : updated.DisplayName
                    };
                }),
                "DocumentLibrary" => TryMapOptional(snapshot.EntityId, libraryMergeMap, value =>
                {
                    updated = updated with
                    {
                        EntityId = value,
                        DisplayName = currentLibrariesById.TryGetValue(value, out var library)
                            ? library.Name
                            : updated.DisplayName
                    };
                }),
                "SitePage" => TryMapOptional(snapshot.EntityId, pageMergeMap, value =>
                {
                    updated = updated with
                    {
                        EntityId = value,
                        DisplayName = currentPagesById.TryGetValue(value, out var page)
                            ? page.Title
                            : updated.DisplayName
                    };
                }),
                _ => false
            };

            return (changed, updated);
        });

        DeduplicateRecords(
            currentWorld.RepositoryAccessGrants,
            grant => $"{Normalize(grant.RepositoryType)}|{Normalize(grant.RepositoryId)}|{Normalize(grant.PrincipalType)}|{Normalize(grant.PrincipalObjectId)}|{Normalize(grant.AccessLevel)}",
            "repository access grants",
            warnings);
        DeduplicateRecords(
            currentWorld.ApplicationRepositoryLinks,
            link => $"{Normalize(link.ApplicationId)}|{Normalize(link.RepositoryType)}|{Normalize(link.RepositoryId)}|{Normalize(link.RelationshipType)}",
            "application repository links",
            warnings);
        DeduplicateRecords(
            currentWorld.CollaborationChannelTabs,
            tab => $"{Normalize(tab.CollaborationChannelId)}|{Normalize(tab.Name)}|{Normalize(tab.TabType)}|{Normalize(tab.TargetType)}|{Normalize(tab.TargetId)}|{Normalize(tab.TargetReference)}",
            "collaboration channel tabs",
            warnings);
        DeduplicateRecords(
            currentWorld.ObservedEntitySnapshots,
            snapshot => $"{Normalize(snapshot.EntityType)}|{Normalize(snapshot.EntityId)}|{Normalize(snapshot.SourceSystem)}|{Normalize(snapshot.DisplayName)}|{Normalize(snapshot.ObservedState)}|{Normalize(snapshot.GroundTruthState)}|{Normalize(snapshot.OwnerReference)}",
            "observed entity snapshots",
            warnings);

        warnings.Add($"Repository merge reconciled {updatedCount} repository binding(s) after regeneration.");

        return new WorldLayerRemapResult
        {
            UpdatedCount = updatedCount,
            Warnings = warnings
        };
    }

    private static int PreserveRepositoryStableIdentifiers(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld, List<string> warnings)
    {
        var updatedCount = 0;

        var siteIdMap = InvertIdMap(BuildIdMap(
            previousWorld.CollaborationSites,
            currentWorld.CollaborationSites,
            site => site.Id,
            site => BuildCollaborationSiteKey(site),
            "collaboration site",
            warnings));
        if (siteIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.CollaborationSites, site =>
            {
                if (!siteIdMap.TryGetValue(site.Id, out var preservedId))
                {
                    return (false, site);
                }

                return (true, site with { Id = preservedId });
            });

            updatedCount += UpdateRecords(currentWorld.CollaborationChannels, channel =>
            {
                var updated = channel;
                var changed = TryMapOptional(channel.CollaborationSiteId, siteIdMap, value => updated = updated with { CollaborationSiteId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.DocumentLibraries, library =>
            {
                var updated = library;
                var changed = TryMapOptional(library.CollaborationSiteId, siteIdMap, value => updated = updated with { CollaborationSiteId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.SitePages, page =>
            {
                var updated = page;
                var changed = TryMapOptional(page.CollaborationSiteId, siteIdMap, value => updated = updated with { CollaborationSiteId = value });
                return (changed, updated);
            });
        }

        var channelIdMap = InvertIdMap(BuildIdMap(
            previousWorld.CollaborationChannels,
            currentWorld.CollaborationChannels,
            channel => channel.Id,
            channel => BuildCollaborationChannelKey(
                channel,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "collaboration channel",
            warnings));
        if (channelIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.CollaborationChannels, channel =>
            {
                if (!channelIdMap.TryGetValue(channel.Id, out var preservedId))
                {
                    return (false, channel);
                }

                return (true, channel with { Id = preservedId });
            });

            updatedCount += UpdateRecords(currentWorld.CollaborationChannelTabs, tab =>
            {
                var updated = tab;
                var changed = TryMapOptional(tab.CollaborationChannelId, channelIdMap, value => updated = updated with { CollaborationChannelId = value });
                if (string.Equals(tab.TargetType, "CollaborationChannel", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= TryMapOptional(tab.TargetId, channelIdMap, value => updated = updated with { TargetId = value });
                }

                return (changed, updated);
            });
        }

        var libraryIdMap = InvertIdMap(BuildIdMap(
            previousWorld.DocumentLibraries,
            currentWorld.DocumentLibraries,
            library => library.Id,
            library => BuildDocumentLibraryKey(
                library,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "document library",
            warnings));
        if (libraryIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.DocumentLibraries, library =>
            {
                if (!libraryIdMap.TryGetValue(library.Id, out var preservedId))
                {
                    return (false, library);
                }

                return (true, library with { Id = preservedId });
            });

            updatedCount += UpdateRecords(currentWorld.SitePages, page =>
            {
                var updated = page;
                var changed = TryMapOptional(page.AssociatedLibraryId, libraryIdMap, value => updated = updated with { AssociatedLibraryId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.DocumentFolders, folder =>
            {
                var updated = folder;
                var changed = TryMapOptional(folder.DocumentLibraryId, libraryIdMap, value => updated = updated with { DocumentLibraryId = value });
                return (changed, updated);
            });

            updatedCount += UpdateRecords(currentWorld.CollaborationChannelTabs, tab =>
            {
                if (!string.Equals(tab.TargetType, "DocumentLibrary", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, tab);
                }

                var updated = tab;
                var changed = TryMapOptional(tab.TargetId, libraryIdMap, value => updated = updated with { TargetId = value });
                return (changed, updated);
            });
        }

        var pageIdMap = InvertIdMap(BuildIdMap(
            previousWorld.SitePages,
            currentWorld.SitePages,
            page => page.Id,
            page => BuildSitePageKey(
                page,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "site page",
            warnings));
        if (pageIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.SitePages, page =>
            {
                if (!pageIdMap.TryGetValue(page.Id, out var preservedId))
                {
                    return (false, page);
                }

                return (true, page with { Id = preservedId });
            });
        }

        var folderIdMap = InvertIdMap(BuildFolderMap(previousWorld, currentWorld, warnings));
        if (folderIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.DocumentFolders, folder =>
            {
                if (!folderIdMap.TryGetValue(folder.Id, out var preservedId))
                {
                    return (false, folder);
                }

                return (true, folder with { Id = preservedId });
            });

            updatedCount += UpdateRecords(currentWorld.DocumentFolders, folder =>
            {
                var updated = folder;
                var changed = TryMapOptional(folder.ParentFolderId, folderIdMap, value => updated = updated with { ParentFolderId = value });
                return (changed, updated);
            });
        }

        var databaseIdMap = InvertIdMap(BuildIdMap(
            previousWorld.Databases,
            currentWorld.Databases,
            database => database.Id,
            database => BuildDatabaseKey(database),
            "database",
            warnings));
        if (databaseIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.Databases, database =>
            {
                if (!databaseIdMap.TryGetValue(database.Id, out var preservedId))
                {
                    return (false, database);
                }

                return (true, database with { Id = preservedId });
            });
        }

        var fileShareIdMap = InvertIdMap(BuildIdMap(
            previousWorld.FileShares,
            currentWorld.FileShares,
            share => share.Id,
            share => BuildFileShareKey(share),
            "file share",
            warnings));
        if (fileShareIdMap.Count > 0)
        {
            updatedCount += UpdateRecords(currentWorld.FileShares, share =>
            {
                if (!fileShareIdMap.TryGetValue(share.Id, out var preservedId))
                {
                    return (false, share);
                }

                return (true, share with { Id = preservedId });
            });
        }

        updatedCount += UpdateRecords(currentWorld.RepositoryAccessGrants, grant =>
        {
            var updated = grant;
            var changed = grant.RepositoryType switch
            {
                "Database" => TryMapOptional(grant.RepositoryId, databaseIdMap, value => updated = updated with { RepositoryId = value }),
                "FileShare" => TryMapOptional(grant.RepositoryId, fileShareIdMap, value => updated = updated with { RepositoryId = value }),
                "CollaborationSite" => TryMapOptional(grant.RepositoryId, siteIdMap, value => updated = updated with { RepositoryId = value }),
                "CollaborationChannel" => TryMapOptional(grant.RepositoryId, channelIdMap, value => updated = updated with { RepositoryId = value }),
                "DocumentLibrary" => TryMapOptional(grant.RepositoryId, libraryIdMap, value => updated = updated with { RepositoryId = value }),
                "SitePage" => TryMapOptional(grant.RepositoryId, pageIdMap, value => updated = updated with { RepositoryId = value }),
                "DocumentFolder" => TryMapOptional(grant.RepositoryId, folderIdMap, value => updated = updated with { RepositoryId = value }),
                _ => false
            };

            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.ApplicationRepositoryLinks, link =>
        {
            var updated = link;
            var changed = link.RepositoryType switch
            {
                "Database" => TryMapOptional(link.RepositoryId, databaseIdMap, value => updated = updated with { RepositoryId = value }),
                "FileShare" => TryMapOptional(link.RepositoryId, fileShareIdMap, value => updated = updated with { RepositoryId = value }),
                "CollaborationSite" => TryMapOptional(link.RepositoryId, siteIdMap, value => updated = updated with { RepositoryId = value }),
                "CollaborationChannel" => TryMapOptional(link.RepositoryId, channelIdMap, value => updated = updated with { RepositoryId = value }),
                "DocumentLibrary" => TryMapOptional(link.RepositoryId, libraryIdMap, value => updated = updated with { RepositoryId = value }),
                "SitePage" => TryMapOptional(link.RepositoryId, pageIdMap, value => updated = updated with { RepositoryId = value }),
                "DocumentFolder" => TryMapOptional(link.RepositoryId, folderIdMap, value => updated = updated with { RepositoryId = value }),
                _ => false
            };

            return (changed, updated);
        });

        updatedCount += UpdateRecords(currentWorld.CollaborationChannelTabs, tab =>
        {
            if (!string.Equals(tab.TargetType, "CollaborationSite", StringComparison.OrdinalIgnoreCase))
            {
                return (false, tab);
            }

            var updated = tab;
            var changed = TryMapOptional(tab.TargetId, siteIdMap, value => updated = updated with { TargetId = value });
            return (changed, updated);
        });

        if (updatedCount > 0)
        {
            warnings.Add($"Repository replacement preserved {updatedCount} stable identifier or reference binding(s) before downstream remap.");
        }

        return updatedCount;
    }

    private static Dictionary<string, string> BuildIdMap<T>(
        IEnumerable<T> previousRecords,
        IEnumerable<T> currentRecords,
        Func<T, string> idSelector,
        Func<T, string?> keySelector,
        string recordType,
        List<string> warnings)
    {
        var previousLookup = BuildLookup(previousRecords, idSelector, keySelector, recordType, "previous", warnings);
        var currentLookup = BuildLookup(currentRecords, idSelector, keySelector, recordType, "current", warnings);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, previousId) in previousLookup)
        {
            if (!currentLookup.TryGetValue(key, out var currentId))
            {
                continue;
            }

            if (!string.Equals(previousId, currentId, StringComparison.OrdinalIgnoreCase))
            {
                map[previousId] = currentId;
            }
        }

        return map;
    }

    private static Dictionary<string, string> BuildMergeMap<T>(
        IEnumerable<T> previousRecords,
        IEnumerable<T> currentRecords,
        Func<T, string> idSelector,
        Func<T, string?> keySelector,
        string recordType,
        List<string> warnings)
    {
        var previousLookup = BuildLookup(previousRecords, idSelector, keySelector, recordType, "previous", warnings);
        var canonicalByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in currentRecords)
        {
            var id = idSelector(record);
            var key = keySelector(record);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (previousLookup.TryGetValue(key, out var preservedId))
            {
                canonicalByKey[key] = preservedId;
                if (!string.Equals(id, preservedId, StringComparison.OrdinalIgnoreCase))
                {
                    map[id] = preservedId;
                }

                continue;
            }

            if (canonicalByKey.TryGetValue(key, out var canonicalId))
            {
                if (!string.Equals(id, canonicalId, StringComparison.OrdinalIgnoreCase))
                {
                    map[id] = canonicalId;
                }

                continue;
            }

            canonicalByKey[key] = id;
        }

        return map;
    }

    private static Dictionary<string, string> BuildLookup<T>(
        IEnumerable<T> records,
        Func<T, string> idSelector,
        Func<T, string?> keySelector,
        string recordType,
        string side,
        List<string> warnings)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var duplicateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var id = idSelector(record);
            var key = keySelector(record);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!lookup.TryAdd(key, id))
            {
                duplicateKeys.Add(key);
            }
        }

        foreach (var duplicateKey in duplicateKeys)
        {
            lookup.Remove(duplicateKey);
        }

        if (duplicateKeys.Count > 0)
        {
            warnings.Add($"Layer remap skipped {duplicateKeys.Count} ambiguous {recordType} key(s) on the {side} side.");
        }

        return lookup;
    }

    private static Dictionary<string, string> InvertIdMap(IReadOnlyDictionary<string, string> map)
        => map.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    private static string? BuildPersonKey(Person person)
    {
        if (!string.IsNullOrWhiteSpace(person.EmployeeId))
        {
            return $"{person.CompanyId}|{Normalize(person.EmploymentType)}|{Normalize(person.EmployeeId)}";
        }

        if (!string.IsNullOrWhiteSpace(person.UserPrincipalName))
        {
            return $"{person.CompanyId}|{Normalize(person.EmploymentType)}|{Normalize(person.UserPrincipalName)}";
        }

        return null;
    }

    private static string? BuildAccountKey(
        DirectoryAccount account,
        IReadOnlyList<Person> previousPeople,
        IReadOnlyList<Person> currentPeople)
    {
        string? personKey = null;
        if (!string.IsNullOrWhiteSpace(account.PersonId))
        {
            personKey = previousPeople.FirstOrDefault(person => string.Equals(person.Id, account.PersonId, StringComparison.OrdinalIgnoreCase)) is { } previousPerson
                ? BuildPersonKey(previousPerson)
                : currentPeople.FirstOrDefault(person => string.Equals(person.Id, account.PersonId, StringComparison.OrdinalIgnoreCase)) is { } currentPerson
                    ? BuildPersonKey(currentPerson)
                    : null;
        }

        if (!string.IsNullOrWhiteSpace(personKey))
        {
            return $"{account.CompanyId}|{Normalize(account.AccountType)}|{personKey}";
        }

        if (!string.IsNullOrWhiteSpace(account.SamAccountName))
        {
            return $"{account.CompanyId}|{Normalize(account.AccountType)}|{Normalize(account.SamAccountName)}|{Normalize(account.UserPrincipalName)}";
        }

        if (!string.IsNullOrWhiteSpace(account.UserPrincipalName))
        {
            return $"{account.CompanyId}|{Normalize(account.AccountType)}|{Normalize(account.UserPrincipalName)}";
        }

        return null;
    }

    private static string? BuildDatabaseKey(DatabaseRepository database)
        => string.IsNullOrWhiteSpace(database.Name)
            ? null
            : $"{database.CompanyId}|{Normalize(database.Name)}|{Normalize(database.OwnerDepartmentId)}|{Normalize(database.Environment)}|{Normalize(database.Engine)}";

    private static string? BuildServerKey(ServerAsset server)
        => string.IsNullOrWhiteSpace(server.Hostname)
            ? null
            : $"{server.CompanyId}|{Normalize(server.Hostname)}";

    private static string? BuildDeviceKey(ManagedDevice device)
        => string.IsNullOrWhiteSpace(device.Hostname)
            ? null
            : $"{device.CompanyId}|{Normalize(device.Hostname)}";

    private static string? BuildSoftwarePackageKey(SoftwarePackage package)
        => string.IsNullOrWhiteSpace(package.Name)
            ? null
            : $"{Normalize(package.Name)}|{Normalize(package.Vendor)}|{Normalize(package.Version)}|{Normalize(package.Category)}";

    private static string? BuildNetworkAssetKey(NetworkAsset asset)
        => string.IsNullOrWhiteSpace(asset.Hostname)
            ? null
            : $"{asset.CompanyId}|{Normalize(asset.AssetType)}|{Normalize(asset.Hostname)}|{Normalize(asset.OfficeId)}";

    private static string? BuildTelephonyAssetKey(TelephonyAsset asset)
        => $"{asset.CompanyId}|{Normalize(asset.AssetType)}|{Normalize(asset.AssignedPersonId)}|{Normalize(asset.AssignedOfficeId)}";

    private static string? BuildFileShareKey(FileShareRepository share)
        => string.IsNullOrWhiteSpace(share.ShareName)
            ? null
            : $"{share.CompanyId}|{Normalize(share.ShareName)}|{Normalize(share.SharePurpose)}|{Normalize(share.OwnerDepartmentId)}|{Normalize(share.OwnerPersonId)}";

    private static string? BuildCollaborationSiteKey(CollaborationSite site)
        => string.IsNullOrWhiteSpace(site.Name)
            ? null
            : $"{site.CompanyId}|{Normalize(site.Platform)}|{Normalize(site.Name)}|{Normalize(site.OwnerDepartmentId)}|{Normalize(site.WorkspaceType)}";

    private static string? BuildCollaborationChannelKey(
        CollaborationChannel channel,
        IReadOnlyList<CollaborationSite> previousSites,
        IReadOnlyList<CollaborationSite> currentSites)
    {
        var site = previousSites.FirstOrDefault(candidate => string.Equals(candidate.Id, channel.CollaborationSiteId, StringComparison.OrdinalIgnoreCase))
                   ?? currentSites.FirstOrDefault(candidate => string.Equals(candidate.Id, channel.CollaborationSiteId, StringComparison.OrdinalIgnoreCase));
        var siteKey = site is null ? null : BuildCollaborationSiteKey(site);
        if (string.IsNullOrWhiteSpace(siteKey) || string.IsNullOrWhiteSpace(channel.Name))
        {
            return null;
        }

        return $"{siteKey}|{Normalize(channel.Name)}|{Normalize(channel.ChannelType)}";
    }

    private static string? BuildDocumentLibraryKey(
        DocumentLibrary library,
        IReadOnlyList<CollaborationSite> previousSites,
        IReadOnlyList<CollaborationSite> currentSites)
    {
        var site = previousSites.FirstOrDefault(candidate => string.Equals(candidate.Id, library.CollaborationSiteId, StringComparison.OrdinalIgnoreCase))
                   ?? currentSites.FirstOrDefault(candidate => string.Equals(candidate.Id, library.CollaborationSiteId, StringComparison.OrdinalIgnoreCase));
        var siteKey = site is null ? null : BuildCollaborationSiteKey(site);
        if (string.IsNullOrWhiteSpace(siteKey) || string.IsNullOrWhiteSpace(library.Name))
        {
            return null;
        }

        return $"{siteKey}|{Normalize(library.Name)}|{Normalize(library.TemplateType)}";
    }

    private static string? BuildSitePageKey(
        SitePage page,
        IReadOnlyList<CollaborationSite> previousSites,
        IReadOnlyList<CollaborationSite> currentSites)
    {
        var site = previousSites.FirstOrDefault(candidate => string.Equals(candidate.Id, page.CollaborationSiteId, StringComparison.OrdinalIgnoreCase))
                   ?? currentSites.FirstOrDefault(candidate => string.Equals(candidate.Id, page.CollaborationSiteId, StringComparison.OrdinalIgnoreCase));
        var siteKey = site is null ? null : BuildCollaborationSiteKey(site);
        if (string.IsNullOrWhiteSpace(siteKey) || string.IsNullOrWhiteSpace(page.Title))
        {
            return null;
        }

        return $"{siteKey}|{Normalize(page.Title)}|{Normalize(page.PageType)}";
    }

    private static Dictionary<string, string> BuildFolderMap(SyntheticEnterpriseWorld previousWorld, SyntheticEnterpriseWorld currentWorld, List<string> warnings)
    {
        var previousFoldersById = previousWorld.DocumentFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Id))
            .ToDictionary(folder => folder.Id, StringComparer.OrdinalIgnoreCase);
        var currentFoldersById = currentWorld.DocumentFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Id))
            .ToDictionary(folder => folder.Id, StringComparer.OrdinalIgnoreCase);

        return BuildIdMap(
            previousWorld.DocumentFolders,
            currentWorld.DocumentFolders,
            folder => folder.Id,
            folder => BuildDocumentFolderKey(
                folder,
                previousFoldersById,
                currentFoldersById,
                previousWorld.DocumentLibraries,
                currentWorld.DocumentLibraries,
                previousWorld.CollaborationSites,
                currentWorld.CollaborationSites),
            "document folder",
            warnings);
    }

    private static string? BuildDocumentFolderKey(
        DocumentFolder folder,
        IReadOnlyDictionary<string, DocumentFolder> previousFoldersById,
        IReadOnlyDictionary<string, DocumentFolder> currentFoldersById,
        IReadOnlyList<DocumentLibrary> previousLibraries,
        IReadOnlyList<DocumentLibrary> currentLibraries,
        IReadOnlyList<CollaborationSite> previousSites,
        IReadOnlyList<CollaborationSite> currentSites)
    {
        var library = previousLibraries.FirstOrDefault(candidate => string.Equals(candidate.Id, folder.DocumentLibraryId, StringComparison.OrdinalIgnoreCase))
                      ?? currentLibraries.FirstOrDefault(candidate => string.Equals(candidate.Id, folder.DocumentLibraryId, StringComparison.OrdinalIgnoreCase));
        var libraryKey = library is null ? null : BuildDocumentLibraryKey(library, previousSites, currentSites);
        if (string.IsNullOrWhiteSpace(libraryKey) || string.IsNullOrWhiteSpace(folder.Name))
        {
            return null;
        }

        var pathSegments = new Stack<string>();
        var current = folder;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (current is not null && visited.Add(current.Id))
        {
            pathSegments.Push(Normalize(current.Name));
            if (string.IsNullOrWhiteSpace(current.ParentFolderId))
            {
                break;
            }

            current = previousFoldersById.TryGetValue(current.ParentFolderId, out var previousParent)
                ? previousParent
                : currentFoldersById.TryGetValue(current.ParentFolderId, out var currentParent)
                    ? currentParent
                    : null;
        }

        return $"{libraryKey}|{string.Join("/", pathSegments)}|{Normalize(folder.Depth)}";
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();

    private static string ExtractCommonName(string distinguishedName, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(distinguishedName))
        {
            var firstSegment = distinguishedName.Split(',', 2)[0];
            if (firstSegment.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return firstSegment;
            }
        }

        return $"CN={fallback}";
    }

    private static bool TryMapOptional(string? sourceId, IReadOnlyDictionary<string, string> map, Action<string> apply)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || !map.TryGetValue(sourceId, out var mappedId))
        {
            return false;
        }

        apply(mappedId);
        return true;
    }

    private static bool TryMapRequired(string sourceId, IReadOnlyDictionary<string, string> map, Action<string> apply)
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

    private static void RemoveMappedDuplicates<T>(
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
            warnings.Add($"Layer remap removed {removed} duplicate {description} during merge reconciliation.");
        }
    }

    private static void DeduplicateRecords<T>(List<T> records, Func<T, string> keySelector, string description, List<string> warnings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = records.RemoveAll(record => !seen.Add(keySelector(record)));
        if (removed > 0)
        {
            warnings.Add($"Layer remap removed {removed} duplicate {description} after rematerialization.");
        }
    }
}
