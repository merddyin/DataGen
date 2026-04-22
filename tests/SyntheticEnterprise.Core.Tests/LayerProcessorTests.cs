using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class LayerProcessorTests
{
    [Fact]
    public void Generate_Populates_FirstClass_Quality_Report()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Quality Contract Test", 180)
            },
            new CatalogSet());

        Assert.NotNull(result.Quality);
        Assert.NotEmpty(result.Quality.Metrics);
        Assert.NotEmpty(result.Quality.Samples);
        Assert.NotEmpty(result.Quality.Heuristics);
        Assert.InRange(result.Quality.OverallScore, 0m, 100m);
        Assert.All(result.Quality.Warnings, warning => Assert.Contains(warning, result.Warnings));
        Assert.Contains("duplicate_person_upns", result.Quality.Consistency.MetricKeys);
        Assert.Contains("undersized_policy_surface", result.Quality.Realism.MetricKeys);
        Assert.Contains("companies_missing_identity_metadata", result.Quality.Completeness.MetricKeys);
        Assert.Contains("business_process_configuration_items", result.Quality.Exportability.MetricKeys);
        Assert.NotEmpty(result.Quality.Realism.Inputs);
        Assert.NotEmpty(result.Quality.Operational.Inputs);
        Assert.Contains(result.Quality.Operational.Inputs, input => input.Key == "layer_coverage");
        Assert.Contains(result.Quality.Operational.Inputs, input => input.Key == "temporal_event_coverage");
    }

    [Fact]
    public void AddRepositoryLayer_Refreshes_Quality_Report_After_World_Mutation()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var processor = services.GetRequiredService<ILayerProcessor>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Layer Quality Refresh Test", 220)
            },
            new CatalogSet());

        result.World.FileShares.Add(new FileShareRepository
        {
            Id = "FS-LEGACY",
            CompanyId = result.World.Companies[0].Id,
            ShareName = "marketing-share-01"
        });
        result = result with
        {
            Quality = new WorldQualityReport
            {
                Metrics = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["generic_share_names"] = 0
                }
            }
        };

        var replaced = processor.AddRepositoryLayer(result, new LayerProcessingOptions
        {
            RepositoryMode = LayerRegenerationMode.ReplaceLayer
        });

        Assert.NotNull(replaced.Quality);
        Assert.True(replaced.Quality.Metrics.ContainsKey("generic_share_names"));
        Assert.NotEmpty(replaced.Quality.Metrics);
        Assert.NotEmpty(replaced.Quality.Exportability.Inputs);
        Assert.NotEmpty(replaced.Quality.Operational.Inputs);
        Assert.InRange(replaced.Quality.OverallScore, 0m, 100m);
        Assert.All(replaced.Quality.Warnings, warning => Assert.Contains(warning, replaced.Warnings));
    }

    [Fact]
    public void Generate_Flags_Enabled_Packs_That_Do_Not_Materialize_Artifacts()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var scenario = BuildScenario("Pack Quality Contract Test", 180) with
        {
            Packs = new ScenarioPackProfile
            {
                EnabledPacks =
                [
                    new ScenarioPackSelection
                    {
                        PackId = "quality.test.pack",
                        Enabled = true
                    }
                ]
            }
        };

        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = scenario
            },
            new CatalogSet());

        var packCoverage = Assert.Single(result.Quality.Operational.Inputs, input => input.Key == "enabled_pack_artifact_coverage");
        Assert.Equal(1m, packCoverage.TargetValue);
        Assert.Equal("packs", packCoverage.Unit);
    }

    [Fact]
    public void AddRepositoryLayer_ReplaceLayer_Clears_Expanded_Repository_Artifacts()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var processor = services.GetRequiredService<ILayerProcessor>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Repository Replace Test", 180)
            },
            new CatalogSet());

        result.World.CollaborationChannelTabs.Add(new CollaborationChannelTab
        {
            Id = "TAB-STALE",
            CompanyId = result.World.Companies[0].Id,
            CollaborationChannelId = "CHAN-STALE",
            Name = "Stale Tab",
            TabType = "Website",
            TargetType = "ExternalUrl",
            TargetReference = "https://stale.invalid"
        });
        result.World.SitePages.Add(new SitePage
        {
            Id = "PAGE-STALE",
            CompanyId = result.World.Companies[0].Id,
            CollaborationSiteId = "SITE-STALE",
            Title = "Stale Page",
            AuthorPersonId = result.World.People[0].Id
        });
        result.World.DocumentFolders.Add(new DocumentFolder
        {
            Id = "FOLDER-STALE",
            CompanyId = result.World.Companies[0].Id,
            DocumentLibraryId = "LIB-STALE",
            Name = "Stale Folder",
            Depth = "1"
        });
        result.World.ApplicationRepositoryLinks.Add(new ApplicationRepositoryLink
        {
            Id = "ARL-STALE",
            CompanyId = result.World.Companies[0].Id,
            ApplicationId = result.World.Applications[0].Id,
            RepositoryId = "LIB-STALE",
            RepositoryType = "DocumentLibrary",
            RelationshipType = "Stale"
        });

        var replaced = processor.AddRepositoryLayer(result, new LayerProcessingOptions
        {
            RepositoryMode = LayerRegenerationMode.ReplaceLayer
        });

        Assert.DoesNotContain(replaced.World.CollaborationChannelTabs, tab => tab.Id == "TAB-STALE");
        Assert.DoesNotContain(replaced.World.SitePages, page => page.Id == "PAGE-STALE");
        Assert.DoesNotContain(replaced.World.DocumentFolders, folder => folder.Id == "FOLDER-STALE");
        Assert.DoesNotContain(replaced.World.ApplicationRepositoryLinks, link => link.Id == "ARL-STALE");
        Assert.NotEmpty(replaced.World.CollaborationChannelTabs);
        Assert.NotEmpty(replaced.World.SitePages);
        Assert.NotEmpty(replaced.World.DocumentFolders);
    }

    [Fact]
    public void AddIdentityLayer_ReplaceLayer_Clears_Stale_External_Workforce_Artifacts()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var processor = services.GetRequiredService<ILayerProcessor>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Identity Replace Test", 220)
            },
            new CatalogSet());

        result.World.People.Add(new Person
        {
            Id = "PERS-STALE",
            CompanyId = result.World.Companies[0].Id,
            TeamId = result.World.Teams[0].Id,
            DepartmentId = result.World.Departments[0].Id,
            FirstName = "Stale",
            LastName = "Contractor",
            DisplayName = "Stale Contractor",
            Title = "Consultant",
            EmployeeId = "CNT-STALE",
            Country = "United States",
            UserPrincipalName = "stale.contractor@replace.test",
            EmploymentType = "Contractor",
            PersonType = "InternalContractor"
        });
        result.World.Accounts.Add(new DirectoryAccount
        {
            Id = "ACT-STALE",
            CompanyId = result.World.Companies[0].Id,
            PersonId = "PERS-STALE",
            AccountType = "Contractor",
            SamAccountName = "stalectr",
            UserPrincipalName = "stale.contractor@replace.test",
            DistinguishedName = "CN=Stale Contractor,OU=Contractors,DC=replace,DC=test",
            OuId = result.World.OrganizationalUnits.First().Id
        });

        var replaced = processor.AddIdentityLayer(result, new LayerProcessingOptions
        {
            IdentityMode = LayerRegenerationMode.ReplaceLayer
        });

        Assert.DoesNotContain(replaced.World.People, person => person.Id == "PERS-STALE");
        Assert.DoesNotContain(replaced.World.Accounts, account => account.Id == "ACT-STALE");
        Assert.Contains(replaced.World.People, person => person.EmploymentType == "Contractor");
        Assert.Contains(replaced.World.Accounts, account => account.AccountType == "Guest");
        Assert.Contains(replaced.World.Groups, group => group.Name == "GG B2B Guests");
    }

    [Fact]
    public void AddIdentityLayer_ReplaceLayer_Remap_Preserves_Dependent_References()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var processor = services.GetRequiredService<ILayerProcessor>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Identity Remap Test", 260)
            },
            new CatalogSet());

        var originalDevice = result.World.Devices.First(device => device.DirectoryAccountId is not null);
        var originalGrantCount = result.World.RepositoryAccessGrants.Count;
        var originalAccountSnapshotCount = result.World.ObservedEntitySnapshots.Count(snapshot => snapshot.EntityType == "Account");
        var originalEndpointAssignmentCount = result.World.EndpointAdministrativeAssignments.Count;
        var originalLocalGroupMemberCount = result.World.EndpointLocalGroupMembers.Count;
        var originalCrossTenantEventCount = result.World.CrossTenantAccessEvents.Count;
        var originalAccountMembershipCount = result.World.GroupMemberships.Count(membership =>
            string.Equals(membership.MemberObjectType, "Account", StringComparison.OrdinalIgnoreCase));
        var originalGuestAccountIds = result.World.Accounts
            .Where(account => string.Equals(account.AccountType, "Guest", StringComparison.OrdinalIgnoreCase))
            .Select(account => account.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var replaced = processor.AddIdentityLayer(result, new LayerProcessingOptions
        {
            IdentityMode = LayerRegenerationMode.ReplaceLayer
        });

        var remappedDevice = replaced.World.Devices.First(device => device.Id == originalDevice.Id);
        var remappedAccount = Assert.Single(replaced.World.Accounts, account => account.Id == remappedDevice.DirectoryAccountId);
        var remappedOu = Assert.Single(replaced.World.OrganizationalUnits, ou => ou.Id == remappedDevice.OuId);
        var accountSnapshots = replaced.World.ObservedEntitySnapshots.Where(snapshot => snapshot.EntityType == "Account").ToList();

        Assert.Equal(remappedDevice.AssignedPersonId, remappedAccount.PersonId);
        Assert.EndsWith(remappedOu.DistinguishedName, remappedDevice.DistinguishedName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(replaced.World.Accounts, account => originalGuestAccountIds.Contains(account.Id));
        Assert.Equal(originalGrantCount, replaced.World.RepositoryAccessGrants.Count);
        Assert.Equal(originalEndpointAssignmentCount, replaced.World.EndpointAdministrativeAssignments.Count);
        Assert.Equal(originalLocalGroupMemberCount, replaced.World.EndpointLocalGroupMembers.Count);
        Assert.True(originalCrossTenantEventCount > 0);
        Assert.NotEmpty(replaced.World.CrossTenantAccessEvents);
        Assert.True(
            replaced.World.CrossTenantAccessEvents.Count(accessEvent =>
                string.Equals(accessEvent.EventType, "AccessStateEvaluated", StringComparison.OrdinalIgnoreCase)) >=
            replaced.World.Accounts.Count(account =>
                string.Equals(account.AccountType, "Guest", StringComparison.OrdinalIgnoreCase)));
        Assert.True(replaced.World.GroupMemberships.Count(membership =>
            string.Equals(membership.MemberObjectType, "Account", StringComparison.OrdinalIgnoreCase)) <= originalAccountMembershipCount);
        Assert.All(replaced.World.RepositoryAccessGrants, grant =>
        {
            if (string.Equals(grant.PrincipalType, "Account", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(replaced.World.Accounts, account => account.Id == grant.PrincipalObjectId);
            }
            else
            {
                Assert.Contains(replaced.World.Groups, group => group.Id == grant.PrincipalObjectId);
            }
        });
        Assert.All(replaced.World.EndpointAdministrativeAssignments, assignment =>
        {
            if (string.Equals(assignment.PrincipalType, "Account", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(replaced.World.Accounts, account => account.Id == assignment.PrincipalObjectId);
            }
            else
            {
                Assert.Contains(replaced.World.Groups, group => group.Id == assignment.PrincipalObjectId);
            }
        });
        Assert.All(replaced.World.EndpointLocalGroupMembers, member =>
        {
            if (!string.IsNullOrWhiteSpace(member.PrincipalObjectId) && string.Equals(member.PrincipalType, "Account", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(replaced.World.Accounts, account => account.Id == member.PrincipalObjectId);
            }
            else if (!string.IsNullOrWhiteSpace(member.PrincipalObjectId) && string.Equals(member.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(replaced.World.Groups, group => group.Id == member.PrincipalObjectId);
            }
        });
        Assert.All(replaced.World.CrossTenantAccessEvents, accessEvent =>
        {
            Assert.Contains(replaced.World.Accounts, account => account.Id == accessEvent.AccountId);
            Assert.Contains(replaced.World.ExternalOrganizations, organization => organization.Id == accessEvent.ExternalOrganizationId);
            if (!string.IsNullOrWhiteSpace(accessEvent.ActorAccountId))
            {
                Assert.Contains(replaced.World.Accounts, account => account.Id == accessEvent.ActorAccountId);
            }
        });
        Assert.True(accountSnapshots.Count <= originalAccountSnapshotCount);
        Assert.All(accountSnapshots, snapshot => Assert.Contains(replaced.World.Accounts, account => account.Id == snapshot.EntityId));
    }

    [Fact]
    public void AddIdentityLayer_Merge_Reconciles_Duplicate_Principals_And_Governance_Artifacts()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var processor = services.GetRequiredService<ILayerProcessor>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Identity Merge Test", 260)
            },
            new CatalogSet());

        var originalPersonCount = result.World.People.Count;
        var originalAccountCount = result.World.Accounts.Count;
        var originalGroupCount = result.World.Groups.Count;
        var originalOuCount = result.World.OrganizationalUnits.Count;
        var originalMembershipCount = result.World.GroupMemberships.Count;
        var originalCrossTenantPolicyCount = result.World.CrossTenantAccessPolicies.Count;
        var originalGuestCount = result.World.Accounts.Count(account =>
            string.Equals(account.AccountType, "Guest", StringComparison.OrdinalIgnoreCase));
        var originalGrantCount = result.World.RepositoryAccessGrants.Count;
        var originalEndpointAssignmentCount = result.World.EndpointAdministrativeAssignments.Count;

        var merged = processor.AddIdentityLayer(result, new LayerProcessingOptions
        {
            IdentityMode = LayerRegenerationMode.Merge
        });

        Assert.Equal(originalPersonCount, merged.World.People.Count);
        Assert.Equal(originalAccountCount, merged.World.Accounts.Count);
        Assert.Equal(originalGroupCount, merged.World.Groups.Count);
        Assert.Equal(originalOuCount, merged.World.OrganizationalUnits.Count);
        Assert.Equal(originalMembershipCount, merged.World.GroupMemberships.Count);
        Assert.Equal(originalCrossTenantPolicyCount, merged.World.CrossTenantAccessPolicies.Count);
        Assert.Equal(originalGuestCount, merged.World.Accounts.Count(account =>
            string.Equals(account.AccountType, "Guest", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(originalGrantCount, merged.World.RepositoryAccessGrants.Count);
        Assert.Equal(originalEndpointAssignmentCount, merged.World.EndpointAdministrativeAssignments.Count);
        Assert.Contains(merged.Warnings, warning => warning.Contains("merge", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            merged.World.CrossTenantAccessEvents.Count,
            merged.World.CrossTenantAccessEvents
                .Select(accessEvent => string.Join("|",
                    accessEvent.AccountId,
                    accessEvent.ExternalOrganizationId,
                    accessEvent.EventType,
                    accessEvent.EventCategory))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(merged.World.RepositoryAccessGrants, grant =>
        {
            if (string.Equals(grant.PrincipalType, "Account", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(merged.World.Accounts, account => account.Id == grant.PrincipalObjectId);
            }
            else
            {
                Assert.Contains(merged.World.Groups, group => group.Id == grant.PrincipalObjectId);
            }
        });
        Assert.All(merged.World.CrossTenantAccessEvents, accessEvent =>
        {
            Assert.Contains(merged.World.Accounts, account => account.Id == accessEvent.AccountId);
            Assert.Contains(merged.World.ExternalOrganizations, organization => organization.Id == accessEvent.ExternalOrganizationId);
            if (!string.IsNullOrWhiteSpace(accessEvent.PolicyId))
            {
                Assert.Contains(merged.World.CrossTenantAccessPolicies, policy => policy.Id == accessEvent.PolicyId);
            }
        });
    }

    [Fact]
    public void AddInfrastructureLayer_ReplaceLayer_Remap_Preserves_Host_References()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var processor = services.GetRequiredService<ILayerProcessor>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Infrastructure Remap Test", 260)
            },
            new CatalogSet());

        var originalServerHostedCount = result.World.ApplicationServiceHostings.Count(hosting =>
            string.Equals(hosting.HostType, "Server", StringComparison.OrdinalIgnoreCase) &&
            hosting.HostId is not null);
        var originalDatabaseHostCount = result.World.Databases.Count(database => database.HostServerId is not null);
        var originalFileShareHostCount = result.World.FileShares.Count(share => share.HostServerId is not null);
        var originalServerSnapshotCount = result.World.ObservedEntitySnapshots.Count(snapshot => snapshot.EntityType == "Server");
        var originalDeviceSnapshotCount = result.World.ObservedEntitySnapshots.Count(snapshot => snapshot.EntityType == "Device");
        var originalComputerMembershipCount = result.World.GroupMemberships.Count(membership =>
            string.Equals(membership.MemberObjectType, "Account", StringComparison.OrdinalIgnoreCase)
            && result.World.Accounts.Any(account =>
                string.Equals(account.Id, membership.MemberObjectId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(account.AccountType, "Device", StringComparison.OrdinalIgnoreCase)));
        var originalPolicyBaselineCount = result.World.EndpointPolicyBaselines.Count;
        var originalLocalGroupMemberCount = result.World.EndpointLocalGroupMembers.Count;
        var originalDeviceSoftwareInstallationCount = result.World.DeviceSoftwareInstallations.Count;
        var originalServerSoftwareInstallationCount = result.World.ServerSoftwareInstallations.Count;
        var originalServerIds = result.World.Servers.Select(server => server.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var originalDeviceIds = result.World.Devices.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replaced = processor.AddInfrastructureLayer(result, new LayerProcessingOptions
        {
            InfrastructureMode = LayerRegenerationMode.ReplaceLayer
        });

        Assert.Equal(originalServerHostedCount, replaced.World.ApplicationServiceHostings.Count(hosting =>
            string.Equals(hosting.HostType, "Server", StringComparison.OrdinalIgnoreCase) &&
            hosting.HostId is not null));
        Assert.All(
            replaced.World.ApplicationServiceHostings.Where(hosting =>
                string.Equals(hosting.HostType, "Server", StringComparison.OrdinalIgnoreCase) &&
                hosting.HostId is not null),
            hosting => Assert.Contains(replaced.World.Servers, server => server.Id == hosting.HostId));
        Assert.Equal(originalDatabaseHostCount, replaced.World.Databases.Count(database => database.HostServerId is not null));
        Assert.All(
            replaced.World.Databases.Where(database => database.HostServerId is not null),
            database => Assert.Contains(replaced.World.Servers, server => server.Id == database.HostServerId));
        Assert.Equal(originalFileShareHostCount, replaced.World.FileShares.Count(share => share.HostServerId is not null));
        Assert.All(
            replaced.World.FileShares.Where(share => share.HostServerId is not null),
            share => Assert.Contains(replaced.World.Servers, server => server.Id == share.HostServerId));
        Assert.True(replaced.World.GroupMemberships.Count(membership =>
            string.Equals(membership.MemberObjectType, "Account", StringComparison.OrdinalIgnoreCase)
            && replaced.World.Accounts.Any(account =>
                string.Equals(account.Id, membership.MemberObjectId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(account.AccountType, "Device", StringComparison.OrdinalIgnoreCase))) >= originalComputerMembershipCount);
        Assert.True(originalDeviceSoftwareInstallationCount > 0);
        Assert.True(originalServerSoftwareInstallationCount > 0);
        Assert.NotEmpty(replaced.World.DeviceSoftwareInstallations);
        Assert.NotEmpty(replaced.World.ServerSoftwareInstallations);
        Assert.True(replaced.World.EndpointAdministrativeAssignments.Count >= replaced.World.Servers.Count);
        Assert.Equal(originalPolicyBaselineCount, replaced.World.EndpointPolicyBaselines.Count);
        Assert.True(replaced.World.EndpointLocalGroupMembers.Count >= replaced.World.Devices.Count + (replaced.World.Servers.Count * 2));
        Assert.Contains(replaced.World.Servers, server => originalServerIds.Contains(server.Id));
        Assert.Contains(replaced.World.Devices, device => originalDeviceIds.Contains(device.Id));
        Assert.All(
            replaced.World.GroupMemberships.Where(membership =>
                string.Equals(membership.MemberObjectType, "Account", StringComparison.OrdinalIgnoreCase)
                && replaced.World.Accounts.Any(account =>
                    string.Equals(account.Id, membership.MemberObjectId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(account.AccountType, "Device", StringComparison.OrdinalIgnoreCase))),
            membership => Assert.Contains(replaced.World.Accounts, account => account.Id == membership.MemberObjectId && account.AccountType == "Device"));
        Assert.All(replaced.World.EndpointAdministrativeAssignments, assignment =>
        {
            if (string.Equals(assignment.EndpointType, "Server", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(replaced.World.Servers, server => server.Id == assignment.EndpointId);
            }
            else
            {
                Assert.Contains(replaced.World.Devices, device => device.Id == assignment.EndpointId);
            }
        });
        Assert.All(replaced.World.EndpointPolicyBaselines, baseline =>
        {
            if (string.Equals(baseline.EndpointType, "Server", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(replaced.World.Servers, server => server.Id == baseline.EndpointId);
            }
            else
            {
                Assert.Contains(replaced.World.Devices, device => device.Id == baseline.EndpointId);
            }
        });
        Assert.All(replaced.World.EndpointLocalGroupMembers, member =>
        {
            if (string.Equals(member.EndpointType, "Server", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(replaced.World.Servers, server => server.Id == member.EndpointId);
            }
            else
            {
                Assert.Contains(replaced.World.Devices, device => device.Id == member.EndpointId);
            }
        });
        Assert.All(replaced.World.DeviceSoftwareInstallations, installation =>
            Assert.Contains(replaced.World.Devices, device => device.Id == installation.DeviceId));
        Assert.All(replaced.World.ServerSoftwareInstallations, installation =>
            Assert.Contains(replaced.World.Servers, server => server.Id == installation.ServerId));

        var serverSnapshots = replaced.World.ObservedEntitySnapshots.Where(snapshot => snapshot.EntityType == "Server").ToList();
        var deviceSnapshots = replaced.World.ObservedEntitySnapshots.Where(snapshot => snapshot.EntityType == "Device").ToList();
        Assert.Equal(originalServerSnapshotCount, serverSnapshots.Count);
        Assert.Equal(originalDeviceSnapshotCount, deviceSnapshots.Count);
        Assert.All(serverSnapshots, snapshot => Assert.Contains(replaced.World.Servers, server => server.Id == snapshot.EntityId));
        Assert.All(deviceSnapshots, snapshot => Assert.Contains(replaced.World.Devices, device => device.Id == snapshot.EntityId));
    }

    [Fact]
    public void AddInfrastructureLayer_Merge_Reconciles_Endpoints_And_Software_Artifacts()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var processor = services.GetRequiredService<ILayerProcessor>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Infrastructure Merge Test", 260)
            },
            new CatalogSet());

        var originalDeviceCount = result.World.Devices.Count;
        var originalServerCount = result.World.Servers.Count;
        var originalNetworkCount = result.World.NetworkAssets.Count;
        var originalTelephonyCount = result.World.TelephonyAssets.Count;
        var originalSoftwareCount = result.World.SoftwarePackages.Count;
        var originalDeviceInstallCount = result.World.DeviceSoftwareInstallations.Count;
        var originalServerInstallCount = result.World.ServerSoftwareInstallations.Count;
        var originalEndpointAssignmentCount = result.World.EndpointAdministrativeAssignments.Count;
        var originalPolicyBaselineCount = result.World.EndpointPolicyBaselines.Count;
        var originalLocalGroupCount = result.World.EndpointLocalGroupMembers.Count;
        var originalComputerMembershipCount = result.World.GroupMemberships.Count(membership =>
            string.Equals(membership.MemberObjectType, "Account", StringComparison.OrdinalIgnoreCase)
            && result.World.Accounts.Any(account =>
                string.Equals(account.Id, membership.MemberObjectId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(account.AccountType, "Device", StringComparison.OrdinalIgnoreCase)));

        var merged = processor.AddInfrastructureLayer(result, new LayerProcessingOptions
        {
            InfrastructureMode = LayerRegenerationMode.Merge
        });

        Assert.Equal(originalDeviceCount, merged.World.Devices.Count);
        Assert.Equal(originalServerCount, merged.World.Servers.Count);
        Assert.Equal(originalNetworkCount, merged.World.NetworkAssets.Count);
        Assert.InRange(merged.World.TelephonyAssets.Count, 1, originalTelephonyCount);
        Assert.Equal(originalSoftwareCount, merged.World.SoftwarePackages.Count);
        Assert.Equal(originalDeviceInstallCount, merged.World.DeviceSoftwareInstallations.Count);
        Assert.Equal(originalServerInstallCount, merged.World.ServerSoftwareInstallations.Count);
        Assert.Equal(originalEndpointAssignmentCount, merged.World.EndpointAdministrativeAssignments.Count);
        Assert.Equal(originalPolicyBaselineCount, merged.World.EndpointPolicyBaselines.Count);
        Assert.Equal(originalLocalGroupCount, merged.World.EndpointLocalGroupMembers.Count);
        Assert.True(merged.World.GroupMemberships.Count(membership =>
            string.Equals(membership.MemberObjectType, "Account", StringComparison.OrdinalIgnoreCase)
            && merged.World.Accounts.Any(account =>
                string.Equals(account.Id, membership.MemberObjectId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(account.AccountType, "Device", StringComparison.OrdinalIgnoreCase))) >= originalComputerMembershipCount);
        Assert.Contains(merged.Warnings, warning => warning.Contains("Infrastructure merge", StringComparison.OrdinalIgnoreCase));
        Assert.All(merged.World.DeviceSoftwareInstallations, installation =>
        {
            Assert.Contains(merged.World.Devices, device => device.Id == installation.DeviceId);
            Assert.Contains(merged.World.SoftwarePackages, package => package.Id == installation.SoftwareId);
        });
        Assert.All(merged.World.ServerSoftwareInstallations, installation =>
        {
            Assert.Contains(merged.World.Servers, server => server.Id == installation.ServerId);
            Assert.Contains(merged.World.SoftwarePackages, package => package.Id == installation.SoftwareId);
        });
        Assert.All(merged.World.EndpointAdministrativeAssignments, assignment =>
        {
            if (string.Equals(assignment.EndpointType, "Server", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(merged.World.Servers, server => server.Id == assignment.EndpointId);
            }
            else
            {
                Assert.Contains(merged.World.Devices, device => device.Id == assignment.EndpointId);
            }
        });
    }

    [Fact]
    public void Generated_World_Metadata_Exposes_Layer_Ownership_Registry()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Ownership Registry Test", 180)
            },
            new CatalogSet());

        Assert.NotNull(result.WorldMetadata);
        Assert.NotEmpty(result.WorldMetadata!.OwnedArtifacts);
        Assert.Contains(result.WorldMetadata.OwnedArtifacts, artifact =>
            artifact.LayerName == "Identity"
            && artifact.EntityType == "DirectoryAccount"
            && artifact.SupportsMergeReconciliation);
        Assert.Contains(result.WorldMetadata.OwnedArtifacts, artifact =>
            artifact.LayerName == "Applications"
            && artifact.EntityType == "ApplicationRecord"
            && artifact.SupportsMergeReconciliation);
        Assert.Contains(result.WorldMetadata.OwnedArtifacts, artifact =>
            artifact.LayerName == "BusinessProcesses"
            && artifact.EntityType == "BusinessProcess"
            && artifact.SupportsMergeReconciliation);
        Assert.Contains(result.WorldMetadata.OwnedArtifacts, artifact =>
            artifact.EntityType == "ExternalOrganization"
            && artifact.OwnershipMode == "Shared"
            && artifact.SupportsMergeReconciliation);
    }

    [Fact]
    public void AddRepositoryLayer_ReplaceLayer_Remap_Preserves_Observed_And_Plugin_References()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var processor = services.GetRequiredService<ILayerProcessor>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Repository Remap Test", 260)
            },
            new CatalogSet());

        var originalFileShare = result.World.FileShares.First();
        var originalSite = result.World.CollaborationSites.First();
        var originalFileShareSnapshotCount = result.World.ObservedEntitySnapshots.Count(snapshot => snapshot.EntityType == "FileShare");
        var originalSiteSnapshotCount = result.World.ObservedEntitySnapshots.Count(snapshot => snapshot.EntityType == "CollaborationSite");

        result.World.PluginRecords.Add(new PluginGeneratedRecord
        {
            Id = "PLUGIN-FS",
            PluginCapability = "RepositoryMetadata",
            RecordType = "FileShareClassification",
            AssociatedEntityType = "FileShare",
            AssociatedEntityId = originalFileShare.Id
        });
        result.World.PluginRecords.Add(new PluginGeneratedRecord
        {
            Id = "PLUGIN-SITE",
            PluginCapability = "RepositoryMetadata",
            RecordType = "SiteMetadata",
            AssociatedEntityType = "CollaborationSite",
            AssociatedEntityId = originalSite.Id
        });

        var replaced = processor.AddRepositoryLayer(result, new LayerProcessingOptions
        {
            RepositoryMode = LayerRegenerationMode.ReplaceLayer
        });

        var fileShareSnapshots = replaced.World.ObservedEntitySnapshots.Where(snapshot => snapshot.EntityType == "FileShare").ToList();
        var siteSnapshots = replaced.World.ObservedEntitySnapshots.Where(snapshot => snapshot.EntityType == "CollaborationSite").ToList();
        var remappedFileShareRecord = Assert.Single(replaced.World.PluginRecords, record => record.Id == "PLUGIN-FS");
        var remappedSiteRecord = Assert.Single(replaced.World.PluginRecords, record => record.Id == "PLUGIN-SITE");

        if (originalFileShareSnapshotCount > 0)
        {
            Assert.InRange(fileShareSnapshots.Count, 1, originalFileShareSnapshotCount);
            Assert.All(fileShareSnapshots, snapshot => Assert.Contains(replaced.World.FileShares, share => share.Id == snapshot.EntityId));
        }

        if (originalSiteSnapshotCount > 0)
        {
            Assert.InRange(siteSnapshots.Count, 1, originalSiteSnapshotCount);
            Assert.All(siteSnapshots, snapshot => Assert.Contains(replaced.World.CollaborationSites, site => site.Id == snapshot.EntityId));
        }
        Assert.Equal(originalFileShare.Id, remappedFileShareRecord.AssociatedEntityId);
        Assert.Contains(replaced.World.FileShares, share => share.Id == remappedFileShareRecord.AssociatedEntityId);
        Assert.True(
            string.IsNullOrWhiteSpace(remappedSiteRecord.AssociatedEntityId)
            || replaced.World.CollaborationSites.Any(site => site.Id == remappedSiteRecord.AssociatedEntityId));
        if (!string.IsNullOrWhiteSpace(remappedSiteRecord.AssociatedEntityId))
        {
            Assert.Equal(originalSite.Id, remappedSiteRecord.AssociatedEntityId);
        }
        Assert.All(replaced.World.ApplicationRepositoryLinks, link =>
        {
            Assert.Contains(replaced.World.Applications, app => app.Id == link.ApplicationId);
            Assert.True(link.RepositoryType switch
            {
                "Database" => replaced.World.Databases.Any(record => record.Id == link.RepositoryId),
                "FileShare" => replaced.World.FileShares.Any(record => record.Id == link.RepositoryId),
                "CollaborationSite" => replaced.World.CollaborationSites.Any(record => record.Id == link.RepositoryId),
                "CollaborationChannel" => replaced.World.CollaborationChannels.Any(record => record.Id == link.RepositoryId),
                "DocumentLibrary" => replaced.World.DocumentLibraries.Any(record => record.Id == link.RepositoryId),
                "SitePage" => replaced.World.SitePages.Any(record => record.Id == link.RepositoryId),
                "DocumentFolder" => replaced.World.DocumentFolders.Any(record => record.Id == link.RepositoryId),
                _ => false
            });
        });
    }

    [Fact]
    public void AddRepositoryLayer_Merge_Reconciles_Repository_Graph_Without_Inflation()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var processor = services.GetRequiredService<ILayerProcessor>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = BuildScenario("Repository Merge Test", 260)
            },
            new CatalogSet());

        var originalDatabaseCount = result.World.Databases.Count;
        var originalFileShareCount = result.World.FileShares.Count;
        var originalSiteCount = result.World.CollaborationSites.Count;
        var originalChannelCount = result.World.CollaborationChannels.Count;
        var originalTabCount = result.World.CollaborationChannelTabs.Count;
        var originalLibraryCount = result.World.DocumentLibraries.Count;
        var originalPageCount = result.World.SitePages.Count;
        var originalFolderCount = result.World.DocumentFolders.Count;
        var originalLinkCount = result.World.ApplicationRepositoryLinks.Count;
        var originalGrantCount = result.World.RepositoryAccessGrants.Count;
        var originalFileShareSnapshotCount = result.World.ObservedEntitySnapshots.Count(snapshot =>
            string.Equals(snapshot.EntityType, "FileShare", StringComparison.OrdinalIgnoreCase));
        var originalSiteSnapshotCount = result.World.ObservedEntitySnapshots.Count(snapshot =>
            string.Equals(snapshot.EntityType, "CollaborationSite", StringComparison.OrdinalIgnoreCase));

        var merged = processor.AddRepositoryLayer(result, new LayerProcessingOptions
        {
            RepositoryMode = LayerRegenerationMode.Merge
        });

        Assert.InRange(merged.World.Databases.Count, 1, originalDatabaseCount);
        Assert.InRange(merged.World.FileShares.Count, 1, originalFileShareCount);
        Assert.InRange(merged.World.CollaborationSites.Count, 1, originalSiteCount);
        Assert.InRange(merged.World.CollaborationChannels.Count, 1, originalChannelCount);
        Assert.InRange(merged.World.CollaborationChannelTabs.Count, 1, originalTabCount);
        Assert.InRange(merged.World.DocumentLibraries.Count, 1, originalLibraryCount);
        Assert.InRange(merged.World.SitePages.Count, 1, originalPageCount);
        Assert.InRange(merged.World.DocumentFolders.Count, 1, originalFolderCount);
        Assert.InRange(merged.World.ApplicationRepositoryLinks.Count, 1, originalLinkCount);
        Assert.InRange(merged.World.RepositoryAccessGrants.Count, 1, originalGrantCount);
        Assert.InRange(
            merged.World.ObservedEntitySnapshots.Count(snapshot =>
                string.Equals(snapshot.EntityType, "FileShare", StringComparison.OrdinalIgnoreCase)),
            1,
            originalFileShareSnapshotCount);
        Assert.InRange(
            merged.World.ObservedEntitySnapshots.Count(snapshot =>
                string.Equals(snapshot.EntityType, "CollaborationSite", StringComparison.OrdinalIgnoreCase)),
            1,
            originalSiteSnapshotCount);
        Assert.Contains(merged.Warnings, warning => warning.Contains("Repository merge", StringComparison.OrdinalIgnoreCase));

        Assert.All(merged.World.RepositoryAccessGrants, grant =>
        {
            Assert.True(grant.RepositoryType switch
            {
                "Database" => merged.World.Databases.Any(record => record.Id == grant.RepositoryId),
                "FileShare" => merged.World.FileShares.Any(record => record.Id == grant.RepositoryId),
                "CollaborationSite" => merged.World.CollaborationSites.Any(record => record.Id == grant.RepositoryId),
                "CollaborationChannel" => merged.World.CollaborationChannels.Any(record => record.Id == grant.RepositoryId),
                "DocumentLibrary" => merged.World.DocumentLibraries.Any(record => record.Id == grant.RepositoryId),
                "SitePage" => merged.World.SitePages.Any(record => record.Id == grant.RepositoryId),
                "DocumentFolder" => merged.World.DocumentFolders.Any(record => record.Id == grant.RepositoryId),
                _ => false
            });
        });

        Assert.All(merged.World.ApplicationRepositoryLinks, link =>
        {
            Assert.Contains(merged.World.Applications, app => app.Id == link.ApplicationId);
            Assert.True(link.RepositoryType switch
            {
                "Database" => merged.World.Databases.Any(record => record.Id == link.RepositoryId),
                "FileShare" => merged.World.FileShares.Any(record => record.Id == link.RepositoryId),
                "CollaborationSite" => merged.World.CollaborationSites.Any(record => record.Id == link.RepositoryId),
                "CollaborationChannel" => merged.World.CollaborationChannels.Any(record => record.Id == link.RepositoryId),
                "DocumentLibrary" => merged.World.DocumentLibraries.Any(record => record.Id == link.RepositoryId),
                "SitePage" => merged.World.SitePages.Any(record => record.Id == link.RepositoryId),
                "DocumentFolder" => merged.World.DocumentFolders.Any(record => record.Id == link.RepositoryId),
                _ => false
            });
        });

        Assert.All(merged.World.CollaborationChannels, channel =>
            Assert.Contains(merged.World.CollaborationSites, site => site.Id == channel.CollaborationSiteId));
        Assert.All(merged.World.DocumentLibraries, library =>
            Assert.Contains(merged.World.CollaborationSites, site => site.Id == library.CollaborationSiteId));
        Assert.All(merged.World.SitePages, page =>
        {
            Assert.Contains(merged.World.CollaborationSites, site => site.Id == page.CollaborationSiteId);
            if (!string.IsNullOrWhiteSpace(page.AssociatedLibraryId))
            {
                Assert.Contains(merged.World.DocumentLibraries, library => library.Id == page.AssociatedLibraryId);
            }
        });
        Assert.All(merged.World.DocumentFolders, folder =>
        {
            Assert.Contains(merged.World.DocumentLibraries, library => library.Id == folder.DocumentLibraryId);
            if (!string.IsNullOrWhiteSpace(folder.ParentFolderId))
            {
                Assert.Contains(merged.World.DocumentFolders, candidate => candidate.Id == folder.ParentFolderId);
            }
        });
        Assert.All(merged.World.CollaborationChannelTabs, tab =>
        {
            Assert.Contains(merged.World.CollaborationChannels, channel => channel.Id == tab.CollaborationChannelId);
            if (string.Equals(tab.TargetType, "CollaborationSite", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(merged.World.CollaborationSites, site => site.Id == tab.TargetId);
            }
            else if (string.Equals(tab.TargetType, "DocumentLibrary", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains(merged.World.DocumentLibraries, library => library.Id == tab.TargetId);
            }
        });
    }

    private static ScenarioDefinition BuildScenario(string name, int employeeCount)
    {
        return new ScenarioDefinition
        {
            Name = name,
            ObservedData = new ObservedDataProfile
            {
                IncludeObservedViews = true,
                CoverageRatio = 0.85
            },
            Applications = new ApplicationProfile
            {
                IncludeApplications = true,
                BaseApplicationCount = 5,
                IncludeLineOfBusinessApplications = true,
                IncludeSaaSApplications = true
            },
            Identity = new IdentityProfile
            {
                IncludeExternalWorkforce = true,
                IncludeB2BGuests = true
            },
            Companies = new()
            {
                new ScenarioCompanyDefinition
                {
                    Name = name.Replace(" ", string.Empty),
                    Industry = "Manufacturing",
                    EmployeeCount = employeeCount,
                    BusinessUnitCount = 2,
                    DepartmentCountPerBusinessUnit = 3,
                    TeamCountPerDepartment = 2,
                    OfficeCount = 2,
                    ServerCount = 8,
                    Countries = new() { "United States" }
                }
            }
        };
    }
}
