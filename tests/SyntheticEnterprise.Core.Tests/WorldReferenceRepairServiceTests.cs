using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Services;

namespace SyntheticEnterprise.Core.Tests;

public sealed class WorldReferenceRepairServiceTests
{
    [Fact]
    public void Repair_Removes_And_Nulls_Invalid_References_Across_World_Graph()
    {
        var world = new SyntheticEnterpriseWorld
        {
            Companies = { new Company { Id = "CO-001", Name = "Contoso", Industry = "Manufacturing" } },
            Departments = { new Department { Id = "DEP-001", CompanyId = "CO-001", BusinessUnitId = "BU-001", Name = "IT" } },
            People = { new Person { Id = "PER-001", CompanyId = "CO-001", DepartmentId = "DEP-001", TeamId = "TEAM-001", FirstName = "Ava", LastName = "Reed", DisplayName = "Ava Reed", Title = "Analyst", EmployeeId = "E001", Country = "United States", UserPrincipalName = "ava.reed@contoso.test" } },
            Accounts = { new DirectoryAccount { Id = "ACT-001", CompanyId = "CO-001", PersonId = "PER-001", OuId = "OU-001", SamAccountName = "areed", UserPrincipalName = "ava.reed@contoso.test", DistinguishedName = "CN=Ava Reed,OU=Users,DC=contoso,DC=test", PreviousInvitedByAccountId = "ACT-MISSING", SponsorLastChangedAt = DateTimeOffset.Parse("2026-04-01T00:00:00Z") } },
            Groups = { new DirectoryGroup { Id = "GRP-001", CompanyId = "CO-001", Name = "GG IT Users", DistinguishedName = "CN=GG IT Users,OU=Groups,DC=contoso,DC=test", OuId = "OU-001" } },
            OrganizationalUnits = { new DirectoryOrganizationalUnit { Id = "OU-001", CompanyId = "CO-001", Name = "Users", DistinguishedName = "OU=Users,DC=contoso,DC=test", Purpose = "Users" } },
            Servers = { new ServerAsset { Id = "SRV-001", CompanyId = "CO-001", Hostname = "srv-app-01", ServerRole = "Application Server", OfficeId = "OFF-001", OwnerTeamId = "TEAM-001" } },
            Devices = { new ManagedDevice { Id = "DEV-001", CompanyId = "CO-001", Hostname = "ws-001", AssignedPersonId = "PER-001", AssignedOfficeId = "OFF-001", DirectoryAccountId = "ACT-001" } },
            Offices = { new Office { Id = "OFF-001", CompanyId = "CO-001", Name = "Chicago", Region = "North America", Country = "United States", City = "Chicago", StateOrProvince = "Illinois", PostalCode = "60601", Latitude = "41.8864", Longitude = "-87.6186", TimeZone = "America/Chicago" } },
            Teams = { new Team { Id = "TEAM-001", CompanyId = "CO-001", DepartmentId = "DEP-001", Name = "Platform" } },
            SoftwarePackages = { new SoftwarePackage { Id = "SW-001", Name = "Defender", Category = "Security", Vendor = "Microsoft", Version = "1.0" } },
            Applications = { new ApplicationRecord { Id = "APP-001", CompanyId = "CO-001", Name = "Portal", Category = "IT", Vendor = "Contoso", BusinessCapability = "IT", HostingModel = "Hybrid", OwnerDepartmentId = "DEP-001" } },
            ApplicationServices = { new ApplicationService { Id = "APPSVC-001", CompanyId = "CO-001", ApplicationId = "APP-001", Name = "Portal API", ServiceType = "API", Runtime = "dotnet", DeploymentModel = "VirtualMachine", OwnerTeamId = "TEAM-001" } },
            CollaborationSites = { new CollaborationSite { Id = "SITE-001", CompanyId = "CO-001", Name = "IT Workspace", Url = "https://collab.contoso.test/sites/it", OwnerPersonId = "PER-001", OwnerDepartmentId = "DEP-001" } },
            DocumentLibraries = { new DocumentLibrary { Id = "LIB-001", CompanyId = "CO-001", CollaborationSiteId = "SITE-001", Name = "Documents" } },
            DocumentFolders = { new DocumentFolder { Id = "FOLD-001", CompanyId = "CO-001", DocumentLibraryId = "LIB-001", Name = "Root", Depth = "1" } },
            CollaborationChannels = { new CollaborationChannel { Id = "CHAN-001", CompanyId = "CO-001", CollaborationSiteId = "SITE-001", Name = "General" } },
            CollaborationChannelTabs = { new CollaborationChannelTab { Id = "TAB-001", CompanyId = "CO-001", CollaborationChannelId = "CHAN-001", Name = "Files", TargetType = "DocumentLibrary", TargetId = "LIB-001", TargetReference = "Documents" } },
            SitePages = { new SitePage { Id = "PAGE-001", CompanyId = "CO-001", CollaborationSiteId = "SITE-001", Title = "Home", AuthorPersonId = "PER-001", AssociatedLibraryId = "LIB-001" } }
        };

        world.ApplicationDependencies.Add(new ApplicationDependency { Id = "APPDEP-001", CompanyId = "CO-001", SourceApplicationId = "APP-001", TargetApplicationId = "APP-MISSING", DependencyType = "Data", InterfaceType = "API" });
        world.RepositoryAccessGrants.Add(new RepositoryAccessGrant { Id = "RAG-001", RepositoryId = "LIB-MISSING", RepositoryType = "DocumentLibrary", PrincipalObjectId = "GRP-001", PrincipalType = "Group", AccessLevel = "Read" });
        world.DocumentFolders.Add(new DocumentFolder { Id = "FOLD-002", CompanyId = "CO-001", DocumentLibraryId = "LIB-MISSING", ParentFolderId = "FOLD-001", Name = "Broken", Depth = "2" });
        world.CollaborationChannelTabs.Add(new CollaborationChannelTab { Id = "TAB-002", CompanyId = "CO-001", CollaborationChannelId = "CHAN-MISSING", Name = "Broken", TargetType = "Application", TargetId = "APP-001", TargetReference = "app://APP-001" });
        world.SitePages.Add(new SitePage { Id = "PAGE-002", CompanyId = "CO-001", CollaborationSiteId = "SITE-001", Title = "Broken", AuthorPersonId = "PER-001", AssociatedLibraryId = "LIB-MISSING" });
        world.Databases.Add(new DatabaseRepository { Id = "DB-001", CompanyId = "CO-001", Name = "ERP", Engine = "SQL Server", OwnerDepartmentId = "DEP-001", AssociatedApplicationId = "APP-MISSING", HostServerId = "SRV-MISSING" });
        world.ObservedEntitySnapshots.Add(new ObservedEntitySnapshot { Id = "OBS-001", CompanyId = "CO-001", SourceSystem = "File Server Inventory", EntityType = "FileShare", EntityId = "FS-MISSING", DisplayName = "\\\\files\\missing", ObservedState = "GroupBased", GroundTruthState = "Department" });
        world.PluginRecords.Add(new PluginGeneratedRecord { Id = "PLUGIN-001", PluginCapability = "RepositoryMetadata", RecordType = "Classification", AssociatedEntityType = "DocumentLibrary", AssociatedEntityId = "LIB-MISSING" });
        world.DeviceSoftwareInstallations.Add(new DeviceSoftwareInstallation { Id = "DSI-001", DeviceId = "DEV-MISSING", SoftwareId = "SW-001" });
        world.DeviceSoftwareInstallations.Add(new DeviceSoftwareInstallation { Id = "DSI-002", DeviceId = "DEV-001", SoftwareId = "SW-MISSING" });
        world.ServerSoftwareInstallations.Add(new ServerSoftwareInstallation { Id = "SSI-001", ServerId = "SRV-MISSING", SoftwareId = "SW-001" });
        world.ServerSoftwareInstallations.Add(new ServerSoftwareInstallation { Id = "SSI-002", ServerId = "SRV-001", SoftwareId = "SW-MISSING" });

        var service = new WorldReferenceRepairService();

        var result = service.Repair(world);

        Assert.True(result.RemovedCount >= 4);
        Assert.True(result.UpdatedCount >= 1);
        Assert.DoesNotContain(world.ApplicationDependencies, dependency => dependency.Id == "APPDEP-001");
        Assert.DoesNotContain(world.RepositoryAccessGrants, grant => grant.Id == "RAG-001");
        Assert.DoesNotContain(world.DocumentFolders, folder => folder.Id == "FOLD-002");
        Assert.DoesNotContain(world.CollaborationChannelTabs, tab => tab.Id == "TAB-002");
        Assert.DoesNotContain(world.SitePages, page => page.Id == "PAGE-002");
        Assert.DoesNotContain(world.ObservedEntitySnapshots, snapshot => snapshot.Id == "OBS-001");
        Assert.Empty(world.DeviceSoftwareInstallations);
        Assert.Empty(world.ServerSoftwareInstallations);
        Assert.Contains(world.Databases, database => database.Id == "DB-001" && database.AssociatedApplicationId is null && database.HostServerId is null);
        Assert.Contains(world.Accounts, account => account.Id == "ACT-001" && account.PreviousInvitedByAccountId is null && account.SponsorLastChangedAt is null);
        Assert.Contains(world.PluginRecords, record => record.Id == "PLUGIN-001" && record.AssociatedEntityType is null && record.AssociatedEntityId is null);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Repair_Does_Not_Flag_Blank_Account_OuIds_As_Invalid()
    {
        var world = new SyntheticEnterpriseWorld
        {
            Companies = { new Company { Id = "CO-001", Name = "Contoso", Industry = "Manufacturing" } },
            Accounts =
            {
                new DirectoryAccount
                {
                    Id = "ACT-DEVICE-001",
                    CompanyId = "CO-001",
                    AccountType = "Device",
                    IdentityProvider = "EntraID",
                    DisplayName = "PAW-CONTOSO-001",
                    SamAccountName = "PAW-CONTOSO-001",
                    UserPrincipalName = "paw-contoso-001@contoso.test",
                    DistinguishedName = "PAW-CONTOSO-001",
                    OuId = string.Empty
                }
            }
        };

        var service = new WorldReferenceRepairService();

        var result = service.Repair(world);

        Assert.Equal(0, result.UpdatedCount);
        Assert.DoesNotContain(result.Warnings, warning =>
            warning.Contains("account OU references", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(world.Accounts, account =>
            account.Id == "ACT-DEVICE-001"
            && string.IsNullOrWhiteSpace(account.OuId)
            && string.Equals(account.DistinguishedName, "PAW-CONTOSO-001", StringComparison.Ordinal));
    }

    [Fact]
    public void Repair_Flags_Blank_Account_OuIds_For_HybridDirectory_Accounts_With_Domain()
    {
        var world = new SyntheticEnterpriseWorld
        {
            Companies = { new Company { Id = "CO-001", Name = "Contoso", Industry = "Manufacturing" } },
            Accounts =
            {
                new DirectoryAccount
                {
                    Id = "ACT-USER-001",
                    CompanyId = "CO-001",
                    AccountType = "User",
                    IdentityProvider = "HybridDirectory",
                    DisplayName = "Ava Reed",
                    SamAccountName = "areed",
                    UserPrincipalName = "ava.reed@contoso.test",
                    Mail = "ava.reed@contoso.test",
                    Domain = "contoso.test",
                    DistinguishedName = "CN=Ava Reed,DC=contoso,DC=test",
                    OuId = string.Empty
                }
            }
        };

        var service = new WorldReferenceRepairService();

        var result = service.Repair(world);

        Assert.Equal(1, result.UpdatedCount);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("account OU references", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(world.Accounts, account =>
            account.Id == "ACT-USER-001"
            && string.IsNullOrWhiteSpace(account.OuId)
            && string.Equals(account.DistinguishedName, "areed", StringComparison.Ordinal));
    }
}
