namespace SyntheticEnterprise.Core.Generation.Observed;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicObservedDataGenerator : IObservedDataGenerator
{
    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;
    private readonly IClock _clock;

    public BasicObservedDataGenerator(IIdFactory idFactory, IRandomSource randomSource, IClock clock)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
        _clock = clock;
    }

    public void GenerateObservedData(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        if (!context.Scenario.ObservedData.IncludeObservedViews)
        {
            return;
        }

        var observedPatterns = ReadObservedSourcePatterns(catalogs);

        foreach (var company in world.Companies)
        {
            GenerateAccountObservations(world, company, context.Scenario.ObservedData, observedPatterns);
            GenerateCrossTenantObservations(world, company, context.Scenario.ObservedData, observedPatterns);
            GenerateDeviceObservations(world, company, context.Scenario.ObservedData, observedPatterns);
            GenerateServerObservations(world, company, context.Scenario.ObservedData, observedPatterns);
            GenerateEndpointControlObservations(world, company, context.Scenario.ObservedData, observedPatterns);
            GenerateRepositoryObservations(world, company, context.Scenario.ObservedData, observedPatterns);
            GenerateApplicationObservations(world, company, context.Scenario.ObservedData, observedPatterns);
            GenerateApplicationServiceObservations(world, company, context.Scenario.ObservedData, observedPatterns);
            GenerateCloudTenantObservations(world, company, context.Scenario.ObservedData, observedPatterns);
        }
    }

    private void GenerateAccountObservations(
        SyntheticEnterpriseWorld world,
        Company company,
        ObservedDataProfile profile,
        IReadOnlyList<ObservedSourcePattern> patterns)
    {
        var companyAccounts = world.Accounts.Where(a => a.CompanyId == company.Id).ToList();
        var peopleCount = world.People.Count(person => person.CompanyId == company.Id);
        var targetCount = CalculateTargetCount(companyAccounts.Count, profile.CoverageRatio);
        var prioritizedAccounts = companyAccounts
            .OrderByDescending(account => string.Equals(account.UserType, "Guest", StringComparison.OrdinalIgnoreCase))
            .ThenBy(account => account.AccountType, StringComparer.OrdinalIgnoreCase)
            .Take(targetCount);

        foreach (var account in prioritizedAccounts)
        {
            var drift = _randomSource.NextDouble() < 0.12;
            var pattern = SelectObservedSourcePattern(
                patterns,
                "Account",
                company.Industry,
                peopleCount,
                applicationName: account.UserPrincipalName,
                applicationCategory: account.AccountType,
                provider: account.UserType,
                serviceType: account.IdentityProvider);
            var groundTruthState = account.UserType == "Guest"
                ? $"{(account.Enabled ? "Enabled" : "Disabled")}/Guest/{account.GuestLifecycleState ?? "Active"}/{account.InvitationStatus ?? "Accepted"}"
                : account.Enabled
                    ? account.MfaEnabled ? "Enabled/MfaEnabled" : "Enabled/MfaDisabled"
                    : "Disabled";
            var observedState = account.UserType == "Guest"
                ? drift
                    ? $"{(account.Enabled ? "Enabled" : "Disabled")}/Guest/{account.GuestLifecycleState ?? "Active"}/PendingAcceptance"
                    : groundTruthState
                : drift && account.MfaEnabled
                    ? "Enabled/MfaUnknown"
                    : groundTruthState;

            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = pattern?.SourceSystem ?? (account.UserType == "Guest" || account.AccountType == "Contractor" || account.AccountType == "ManagedServiceProvider"
                    ? "Entra ID"
                    : account.AccountType == "User" ? "Entra ID" : "Active Directory"),
                EntityType = "Account",
                EntityId = account.Id,
                DisplayName = account.UserPrincipalName,
                ObservedState = observedState,
                GroundTruthState = groundTruthState,
                DriftType = account.UserType == "Guest"
                    ? (drift ? FirstNonEmpty(pattern?.DriftType, "GuestInvitationLag") : "None")
                    : drift ? FirstNonEmpty(pattern?.DriftType, "IdentityStateLag") : "None",
                OwnerReference = account.InvitedOrganizationId ?? account.ManagerAccountId,
                RecordedAt = _clock.UtcNow.AddMinutes(-_randomSource.Next(15, 1440))
            });
        }
    }

    private void GenerateDeviceObservations(
        SyntheticEnterpriseWorld world,
        Company company,
        ObservedDataProfile profile,
        IReadOnlyList<ObservedSourcePattern> patterns)
    {
        var devices = world.Devices.Where(d => d.CompanyId == company.Id).ToList();
        var peopleCount = world.People.Count(person => person.CompanyId == company.Id);
        foreach (var device in devices.Take(CalculateTargetCount(devices.Count, profile.CoverageRatio)))
        {
            var drift = _randomSource.NextDouble() < 0.1;
            var pattern = SelectObservedSourcePattern(
                patterns,
                "Device",
                company.Industry,
                peopleCount,
                applicationName: device.Hostname,
                provider: device.DeviceType,
                applicationCategory: device.ComplianceState);
            var observedState = drift
                ? (device.ComplianceState == "Compliant" ? "Unknown" : "Compliant")
                : device.ComplianceState;

            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = pattern?.SourceSystem ?? "Intune",
                EntityType = "Device",
                EntityId = device.Id,
                DisplayName = device.Hostname,
                ObservedState = observedState,
                GroundTruthState = device.ComplianceState,
                DriftType = drift ? FirstNonEmpty(pattern?.DriftType, "ComplianceLag") : "None",
                OwnerReference = device.DirectoryAccountId,
                RecordedAt = device.LastSeen.AddHours(_randomSource.Next(1, 12))
            });
        }
    }

    private void GenerateCrossTenantObservations(
        SyntheticEnterpriseWorld world,
        Company company,
        ObservedDataProfile profile,
        IReadOnlyList<ObservedSourcePattern> patterns)
    {
        var policies = world.CrossTenantAccessPolicies.Where(policy => policy.CompanyId == company.Id).ToList();
        var peopleCount = world.People.Count(person => person.CompanyId == company.Id);
        foreach (var policy in policies.Take(CalculateTargetCount(policies.Count, profile.CoverageRatio * 0.8)))
        {
            var drift = _randomSource.NextDouble() < 0.08;
            var pattern = SelectObservedSourcePattern(
                patterns,
                "CrossTenantPolicy",
                company.Industry,
                peopleCount,
                applicationName: policy.PolicyName,
                applicationCategory: policy.AllowedResourceScope,
                provider: policy.AccessDirection,
                serviceType: policy.RelationshipType);
            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = pattern?.SourceSystem ?? "Entra Cross-Tenant Access Settings",
                EntityType = "CrossTenantPolicy",
                EntityId = policy.Id,
                DisplayName = policy.PolicyName,
                ObservedState = drift
                    ? $"{policy.DefaultAccess}/PendingTrustSync"
                    : $"{policy.DefaultAccess}/{policy.AllowedResourceScope}",
                GroundTruthState = $"{policy.DefaultAccess}/{policy.AllowedResourceScope}",
                DriftType = drift ? FirstNonEmpty(pattern?.DriftType, "CrossTenantPolicyLag") : "None",
                OwnerReference = policy.ExternalOrganizationId,
                RecordedAt = _clock.UtcNow.AddHours(-_randomSource.Next(2, 72))
            });
        }

        var guestAccounts = world.Accounts
            .Where(account => account.CompanyId == company.Id
                              && string.Equals(account.IdentityProvider, "EntraB2B", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var account in guestAccounts.Take(CalculateTargetCount(guestAccounts.Count, profile.CoverageRatio * 0.7)))
        {
            var drift = _randomSource.NextDouble() < 0.1;
            var pattern = SelectObservedSourcePattern(
                patterns,
                "CrossTenantGuestAccess",
                company.Industry,
                peopleCount,
                applicationName: account.UserPrincipalName,
                applicationCategory: account.AccountType,
                provider: account.IdentityProvider,
                serviceType: account.GuestLifecycleState);
            var groundTruthState = $"{account.EntitlementAssignmentState ?? "ActiveAssignment"}/{account.AccessReviewStatus ?? "Approved"}";
            var observedState = drift
                ? $"{account.EntitlementAssignmentState ?? "ActiveAssignment"}/PendingReviewSync"
                : groundTruthState;

            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = pattern?.SourceSystem ?? "Entra Entitlement Management",
                EntityType = "CrossTenantGuestAccess",
                EntityId = account.Id,
                DisplayName = account.UserPrincipalName,
                ObservedState = observedState,
                GroundTruthState = groundTruthState,
                DriftType = drift ? FirstNonEmpty(pattern?.DriftType, "EntitlementLag") : "None",
                OwnerReference = account.InvitedOrganizationId ?? account.InvitedByAccountId,
                RecordedAt = _clock.UtcNow.AddHours(-_randomSource.Next(4, 96))
            });
        }
    }

    private void GenerateServerObservations(
        SyntheticEnterpriseWorld world,
        Company company,
        ObservedDataProfile profile,
        IReadOnlyList<ObservedSourcePattern> patterns)
    {
        var servers = world.Servers.Where(server => server.CompanyId == company.Id).ToList();
        var peopleCount = world.People.Count(person => person.CompanyId == company.Id);
        foreach (var server in servers.Take(CalculateTargetCount(servers.Count, profile.CoverageRatio)))
        {
            var drift = _randomSource.NextDouble() < 0.08;
            var pattern = SelectObservedSourcePattern(
                patterns,
                "Server",
                company.Industry,
                peopleCount,
                applicationName: server.Hostname,
                provider: server.ServerRole,
                applicationCategory: server.Environment);
            var observedState = drift ? "OwnerUnknown" : server.ServerRole;

            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = pattern?.SourceSystem ?? "CMDB",
                EntityType = "Server",
                EntityId = server.Id,
                DisplayName = server.Hostname,
                ObservedState = observedState,
                GroundTruthState = server.ServerRole,
                DriftType = drift ? FirstNonEmpty(pattern?.DriftType, "OwnershipDrift") : "None",
                Environment = server.Environment,
                OwnerReference = server.OwnerTeamId,
                RecordedAt = _clock.UtcNow.AddHours(-_randomSource.Next(4, 72))
            });
        }
    }

    private void GenerateEndpointControlObservations(
        SyntheticEnterpriseWorld world,
        Company company,
        ObservedDataProfile profile,
        IReadOnlyList<ObservedSourcePattern> patterns)
    {
        var policyBaselines = world.EndpointPolicyBaselines.Where(baseline => baseline.CompanyId == company.Id).ToList();
        var peopleCount = world.People.Count(person => person.CompanyId == company.Id);
        foreach (var baseline in policyBaselines.Take(CalculateTargetCount(policyBaselines.Count, profile.CoverageRatio * 0.6)))
        {
            var drift = _randomSource.NextDouble() < 0.08;
            var pattern = SelectObservedSourcePattern(
                patterns,
                "EndpointPolicy",
                company.Industry,
                peopleCount,
                applicationName: baseline.PolicyName,
                applicationCategory: baseline.PolicyCategory,
                provider: baseline.EndpointType,
                serviceType: baseline.AdministrativeTier);
            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = pattern?.SourceSystem ?? (baseline.EndpointType == "Server" ? "Group Policy Results" : "Intune Policy"),
                EntityType = "EndpointPolicy",
                EntityId = baseline.EndpointId,
                DisplayName = $"{baseline.PolicyName}::{baseline.EndpointId}",
                ObservedState = drift ? "Pending" : baseline.CurrentState,
                GroundTruthState = baseline.DesiredState,
                DriftType = drift ? FirstNonEmpty(pattern?.DriftType, "PolicyApplicationLag") : "None",
                OwnerReference = baseline.AssignedFrom,
                RecordedAt = _clock.UtcNow.AddHours(-_randomSource.Next(2, 48))
            });
        }

        var localGroupMembers = world.EndpointLocalGroupMembers.Where(member => member.CompanyId == company.Id).ToList();
        foreach (var member in localGroupMembers.Take(CalculateTargetCount(localGroupMembers.Count, profile.CoverageRatio * 0.45)))
        {
            var drift = _randomSource.NextDouble() < 0.06;
            var pattern = SelectObservedSourcePattern(
                patterns,
                "EndpointLocalGroup",
                company.Industry,
                peopleCount,
                applicationName: member.PrincipalName,
                applicationCategory: member.LocalGroupName,
                provider: member.EndpointType,
                serviceType: member.AdministrativeTier);
            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = pattern?.SourceSystem ?? (member.EndpointType == "Server" ? "Local Group Inventory" : "Endpoint Security Center"),
                EntityType = "EndpointLocalGroup",
                EntityId = member.EndpointId,
                DisplayName = $"{member.LocalGroupName}::{member.PrincipalName}",
                ObservedState = drift ? "UnexpectedMembership" : member.MembershipSource,
                GroundTruthState = member.LocalGroupName,
                DriftType = drift ? FirstNonEmpty(pattern?.DriftType, "LocalAdminDrift") : "None",
                OwnerReference = member.PrincipalObjectId ?? member.PrincipalName,
                RecordedAt = _clock.UtcNow.AddHours(-_randomSource.Next(4, 72))
            });
        }
    }

    private void GenerateRepositoryObservations(
        SyntheticEnterpriseWorld world,
        Company company,
        ObservedDataProfile profile,
        IReadOnlyList<ObservedSourcePattern> patterns)
    {
        var peopleCount = world.People.Count(person => person.CompanyId == company.Id);
        var fileShares = world.FileShares.Where(share => share.CompanyId == company.Id).ToList();
        foreach (var share in fileShares.Take(CalculateTargetCount(fileShares.Count, profile.CoverageRatio * 0.8)))
        {
            var sharePattern = SelectObservedSourcePattern(
                patterns,
                "FileShare",
                company.Industry,
                peopleCount,
                applicationName: share.ShareName,
                applicationCategory: share.SharePurpose);
            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = sharePattern?.SourceSystem ?? "File Server Inventory",
                EntityType = "FileShare",
                EntityId = share.Id,
                DisplayName = share.UncPath,
                ObservedState = share.AccessModel,
                GroundTruthState = share.SharePurpose,
                DriftType = share.SharePurpose == "UserProfile"
                    ? "ClassificationLag"
                    : FirstNonEmpty(sharePattern?.DriftType, "None"),
                OwnerReference = share.OwnerPersonId ?? share.OwnerDepartmentId,
                RecordedAt = _clock.UtcNow.AddHours(-_randomSource.Next(8, 120))
            });
        }

        var collaborationSites = world.CollaborationSites.Where(site => site.CompanyId == company.Id).ToList();
        foreach (var site in collaborationSites.Take(CalculateTargetCount(collaborationSites.Count, profile.CoverageRatio * 0.8)))
        {
            var sitePattern = SelectObservedSourcePattern(
                patterns,
                "CollaborationSite",
                company.Industry,
                peopleCount,
                provider: site.Platform,
                applicationCategory: site.WorkspaceType);
            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = sitePattern?.SourceSystem ?? (site.Platform == "Teams" ? "Teams Admin Center" : "SharePoint Admin Center"),
                EntityType = "CollaborationSite",
                EntityId = site.Id,
                DisplayName = site.Name,
                ObservedState = site.PrivacyType,
                GroundTruthState = site.WorkspaceType,
                DriftType = _randomSource.NextDouble() < 0.1
                    ? FirstNonEmpty(sitePattern?.DriftType, "WorkspaceClassificationLag")
                    : "None",
                OwnerReference = site.OwnerPersonId,
                RecordedAt = _clock.UtcNow.AddHours(-_randomSource.Next(4, 96))
            });
        }
    }

    private void GenerateApplicationObservations(
        SyntheticEnterpriseWorld world,
        Company company,
        ObservedDataProfile profile,
        IReadOnlyList<ObservedSourcePattern> patterns)
    {
        var applications = world.Applications.Where(application => application.CompanyId == company.Id).ToList();
        var peopleCount = world.People.Count(person => person.CompanyId == company.Id);
        foreach (var application in applications.Take(CalculateTargetCount(applications.Count, profile.CoverageRatio * 0.75)))
        {
            var drift = _randomSource.NextDouble() < 0.1;
            var pattern = SelectObservedSourcePattern(
                patterns,
                "Application",
                company.Industry,
                peopleCount,
                applicationName: application.Name,
                applicationVendor: application.Vendor,
                applicationCategory: application.Category);
            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = pattern?.SourceSystem ?? "Application Portfolio",
                EntityType = "Application",
                EntityId = application.Id,
                DisplayName = application.Name,
                ObservedState = drift ? "OwnerPending" : application.HostingModel,
                GroundTruthState = application.HostingModel,
                DriftType = drift ? FirstNonEmpty(pattern?.DriftType, "ApplicationMetadataLag") : "None",
                Environment = application.Environment,
                OwnerReference = application.OwnerDepartmentId,
                RecordedAt = _clock.UtcNow.AddHours(-_randomSource.Next(12, 168))
            });
        }
    }

    private void GenerateApplicationServiceObservations(
        SyntheticEnterpriseWorld world,
        Company company,
        ObservedDataProfile profile,
        IReadOnlyList<ObservedSourcePattern> patterns)
    {
        var services = world.ApplicationServices.Where(service => service.CompanyId == company.Id).ToList();
        var applicationsById = world.Applications
            .Where(application => application.CompanyId == company.Id)
            .ToDictionary(application => application.Id, StringComparer.OrdinalIgnoreCase);
        var peopleCount = world.People.Count(person => person.CompanyId == company.Id);
        foreach (var service in services.Take(CalculateTargetCount(services.Count, profile.CoverageRatio * 0.65)))
        {
            var drift = _randomSource.NextDouble() < 0.08;
            applicationsById.TryGetValue(service.ApplicationId, out var application);
            var pattern = SelectObservedSourcePattern(
                patterns,
                "ApplicationService",
                company.Industry,
                peopleCount,
                applicationName: service.Name,
                applicationVendor: application?.Vendor,
                applicationCategory: application?.Category,
                serviceType: service.ServiceType,
                deploymentModel: service.DeploymentModel);
            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = pattern?.SourceSystem ?? service.DeploymentModel switch
                {
                    "SaaSPlatform" => "SaaS Admin Console",
                    "ManagedPlatform" => "Managed Platform Inventory",
                    _ => "Application Performance Monitor"
                },
                EntityType = "ApplicationService",
                EntityId = service.Id,
                DisplayName = service.Name,
                ObservedState = drift ? "OwnerPending" : $"{service.DeploymentModel}/{service.Runtime}",
                GroundTruthState = $"{service.DeploymentModel}/{service.Runtime}",
                DriftType = drift ? FirstNonEmpty(pattern?.DriftType, "ServiceMetadataLag") : "None",
                Environment = service.Environment,
                OwnerReference = service.OwnerTeamId,
                RecordedAt = _clock.UtcNow.AddHours(-_randomSource.Next(6, 96))
            });
        }
    }

    private void GenerateCloudTenantObservations(
        SyntheticEnterpriseWorld world,
        Company company,
        ObservedDataProfile profile,
        IReadOnlyList<ObservedSourcePattern> patterns)
    {
        var tenants = world.CloudTenants.Where(tenant => tenant.CompanyId == company.Id).ToList();
        var peopleCount = world.People.Count(person => person.CompanyId == company.Id);
        foreach (var tenant in tenants.Take(CalculateTargetCount(tenants.Count, profile.CoverageRatio * 0.8)))
        {
            var drift = _randomSource.NextDouble() < 0.06;
            var pattern = SelectObservedSourcePattern(
                patterns,
                "CloudTenant",
                company.Industry,
                peopleCount,
                provider: tenant.Provider);
            world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot
            {
                Id = _idFactory.Next("OBS"),
                CompanyId = company.Id,
                SourceSystem = pattern?.SourceSystem ?? tenant.Provider switch
                {
                    "Microsoft" => "Microsoft Entra Admin Center",
                    "Google" => "Google Admin Console",
                    "Salesforce" => "Salesforce Setup",
                    "Workday" => "Workday Admin Console",
                    _ => "Cloud Tenant Inventory"
                },
                EntityType = "CloudTenant",
                EntityId = tenant.Id,
                DisplayName = tenant.Name,
                ObservedState = drift ? $"{tenant.AuthenticationModel}/PendingDomainValidation" : tenant.AuthenticationModel,
                GroundTruthState = tenant.AuthenticationModel,
                DriftType = drift ? FirstNonEmpty(pattern?.DriftType, "TenantConfigurationLag") : "None",
                Environment = tenant.Environment,
                OwnerReference = tenant.AdminDepartmentId,
                RecordedAt = _clock.UtcNow.AddHours(-_randomSource.Next(8, 120))
            });
        }
    }

    private static IReadOnlyList<ObservedSourcePattern> ReadObservedSourcePatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("observed_source_patterns", out var rows))
        {
            return Array.Empty<ObservedSourcePattern>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "EntityType")))
            .Select(row => new ObservedSourcePattern(
                Read(row, "EntityType"),
                Read(row, "MatchNameContains"),
                Read(row, "MatchVendor"),
                Read(row, "MatchProvider"),
                Read(row, "MatchCategory"),
                Read(row, "MatchServiceType"),
                Read(row, "MatchDeploymentModel"),
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "SourceSystem"),
                Read(row, "DriftType")))
            .ToList();
    }

    private static ObservedSourcePattern? SelectObservedSourcePattern(
        IReadOnlyList<ObservedSourcePattern> patterns,
        string entityType,
        string? industry,
        int peopleCount,
        string? applicationName = null,
        string? applicationVendor = null,
        string? applicationCategory = null,
        string? provider = null,
        string? serviceType = null,
        string? deploymentModel = null)
    {
        var industryTokens = SplitIndustryTokens(industry);

        return patterns
            .Where(pattern => string.Equals(pattern.EntityType, entityType, StringComparison.OrdinalIgnoreCase))
            .Where(pattern => pattern.MinimumEmployees <= Math.Max(1, peopleCount))
            .Where(pattern => pattern.IndustryTags.Count == 0
                              || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                              || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .Where(pattern => MatchesObservedSourcePattern(
                pattern,
                applicationName,
                applicationVendor,
                applicationCategory,
                provider,
                serviceType,
                deploymentModel))
            .OrderByDescending(GetObservedSourcePatternSpecificity)
            .FirstOrDefault();
    }

    private static bool MatchesObservedSourcePattern(
        ObservedSourcePattern pattern,
        string? applicationName,
        string? applicationVendor,
        string? applicationCategory,
        string? provider,
        string? serviceType,
        string? deploymentModel)
    {
        if (!string.IsNullOrWhiteSpace(pattern.MatchNameContains)
            && !(applicationName?.Contains(pattern.MatchNameContains, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor)
            && !string.Equals(applicationVendor, pattern.MatchVendor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchProvider)
            && !string.Equals(provider, pattern.MatchProvider, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory)
            && !string.Equals(applicationCategory, pattern.MatchCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchServiceType)
            && !string.Equals(serviceType, pattern.MatchServiceType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchDeploymentModel)
            && !string.Equals(deploymentModel, pattern.MatchDeploymentModel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int GetObservedSourcePatternSpecificity(ObservedSourcePattern pattern)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(pattern.MatchNameContains))
        {
            score += 5;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchProvider))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchServiceType))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchDeploymentModel))
        {
            score += 1;
        }

        if (pattern.IndustryTags.Count > 0 && !pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }

    private static HashSet<string> SplitIndustryTokens(string? industry)
    {
        if (string.IsNullOrWhiteSpace(industry))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return industry
            .Split(['|', ',', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> SplitPipeSeparated(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static string Read(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static int CalculateTargetCount(int sourceCount, double coverageRatio)
        => Math.Max(1, (int)Math.Round(sourceCount * Math.Clamp(coverageRatio, 0.1, 1.0)));

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed record ObservedSourcePattern(
        string EntityType,
        string MatchNameContains,
        string MatchVendor,
        string MatchProvider,
        string MatchCategory,
        string MatchServiceType,
        string MatchDeploymentModel,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string SourceSystem,
        string DriftType);
}
