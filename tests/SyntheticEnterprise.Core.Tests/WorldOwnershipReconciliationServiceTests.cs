using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Services;

namespace SyntheticEnterprise.Core.Tests;

public sealed class WorldOwnershipReconciliationServiceTests
{
    [Fact]
    public void Reconcile_Merges_Shared_Application_Process_Tenant_And_Ecosystem_Artifacts()
    {
        var world = new SyntheticEnterpriseWorld
        {
            Companies = { new Company { Id = "CO-001", Name = "Contoso", Industry = "Manufacturing" } },
            Departments =
            {
                new Department { Id = "DEP-001", CompanyId = "CO-001", BusinessUnitId = "BU-001", Name = "IT" },
                new Department { Id = "DEP-002", CompanyId = "CO-001", BusinessUnitId = "BU-001", Name = "Operations" }
            },
            Teams =
            {
                new Team { Id = "TEAM-001", CompanyId = "CO-001", DepartmentId = "DEP-001", Name = "Platform" }
            },
            Applications =
            {
                new ApplicationRecord { Id = "APP-001", CompanyId = "CO-001", Name = "Portal", Category = "Business", Vendor = "Contoso", BusinessCapability = "Operations", HostingModel = "Hybrid", Environment = "Production", OwnerDepartmentId = "DEP-001" },
                new ApplicationRecord { Id = "APP-002", CompanyId = "CO-001", Name = "Portal", Category = "Business", Vendor = "Contoso", BusinessCapability = "Operations", HostingModel = "Hybrid", Environment = "Production", OwnerDepartmentId = "DEP-001" }
            },
            BusinessProcesses =
            {
                new BusinessProcess { Id = "PROC-001", CompanyId = "CO-001", Name = "Order to Cash", Domain = "Revenue", BusinessCapability = "Revenue Operations", OwnerDepartmentId = "DEP-002", OperatingModel = "Regional", ProcessScope = "Enterprise", Criticality = "High" },
                new BusinessProcess { Id = "PROC-002", CompanyId = "CO-001", Name = "Order to Cash", Domain = "Revenue", BusinessCapability = "Revenue Operations", OwnerDepartmentId = "DEP-002", OperatingModel = "Regional", ProcessScope = "Enterprise", Criticality = "High" }
            },
            CloudTenants =
            {
                new CloudTenant { Id = "TEN-001", CompanyId = "CO-001", Provider = "Microsoft", TenantType = "PrimaryWorkspace", Name = "Contoso Productivity", PrimaryDomain = "contoso.com", Region = "North America", AuthenticationModel = "Federated", AdminDepartmentId = "DEP-001" },
                new CloudTenant { Id = "TEN-002", CompanyId = "CO-001", Provider = "Microsoft", TenantType = "PrimaryWorkspace", Name = "Contoso Productivity", PrimaryDomain = "contoso.com", Region = "North America", AuthenticationModel = "Federated", AdminDepartmentId = "DEP-001" }
            },
            ExternalOrganizations =
            {
                new ExternalOrganization { Id = "ORG-001", CompanyId = "CO-001", Name = "Northwind Supply", RelationshipType = "Vendor", Industry = "Industrial", Country = "United States", PrimaryDomain = "northwind.example", ContactEmail = "ops@northwind.example", OwnerDepartmentId = "DEP-002", Segment = "StrategicSupplier", RevenueBand = "Large" },
                new ExternalOrganization { Id = "ORG-002", CompanyId = "CO-001", Name = "Northwind Supply", RelationshipType = "Vendor", Industry = "Industrial", Country = "United States", PrimaryDomain = "northwind.example", ContactEmail = "support@northwind.example", OwnerDepartmentId = "DEP-002", Segment = "StrategicSupplier", RevenueBand = "Large" }
            },
            People =
            {
                new Person { Id = "PER-001", CompanyId = "CO-001", TeamId = "TEAM-001", DepartmentId = "DEP-001", FirstName = "Ava", LastName = "Reed", DisplayName = "Ava Reed", Title = "Engineer", EmployeeId = "E001", Country = "United States", UserPrincipalName = "ava.reed@contoso.test", EmploymentType = "Contractor", PersonType = "ExternalContractor", EmployerOrganizationId = "ORG-002" }
            },
            Accounts =
            {
                new DirectoryAccount { Id = "ACT-001", CompanyId = "CO-001", PersonId = "PER-001", AccountType = "Guest", SamAccountName = "ava.reed", UserPrincipalName = "ava.reed_northwind#EXT#@contoso.test", DistinguishedName = "CN=Ava Reed,OU=Guests,DC=contoso,DC=test", OuId = "OU-001", InvitedOrganizationId = "ORG-002" }
            },
            ApplicationDependencies =
            {
                new ApplicationDependency { Id = "APPDEP-001", CompanyId = "CO-001", SourceApplicationId = "APP-002", TargetApplicationId = "APP-001", DependencyType = "Data", InterfaceType = "API" }
            },
            ApplicationServices =
            {
                new ApplicationService { Id = "SVC-001", CompanyId = "CO-001", ApplicationId = "APP-001", Name = "Portal API", ServiceType = "API", Runtime = "dotnet", DeploymentModel = "VirtualMachine", Environment = "Production", OwnerTeamId = "TEAM-001" },
                new ApplicationService { Id = "SVC-002", CompanyId = "CO-001", ApplicationId = "APP-002", Name = "Portal API", ServiceType = "API", Runtime = "dotnet", DeploymentModel = "VirtualMachine", Environment = "Production", OwnerTeamId = "TEAM-001" }
            },
            ApplicationServiceDependencies =
            {
                new ApplicationServiceDependency { Id = "SVCDEP-001", CompanyId = "CO-001", SourceServiceId = "SVC-002", TargetServiceId = "SVC-001", DependencyType = "Internal", InterfaceType = "HTTPS" }
            },
            ApplicationServiceHostings =
            {
                new ApplicationServiceHosting { Id = "HOST-001", CompanyId = "CO-001", ApplicationServiceId = "SVC-002", HostType = "Server", HostId = "SRV-001", HostName = "srv-app-01", HostingRole = "Primary", DeploymentModel = "VirtualMachine" }
            },
            ApplicationTenantLinks =
            {
                new ApplicationTenantLink { Id = "TENLINK-001", CompanyId = "CO-001", ApplicationId = "APP-002", CloudTenantId = "TEN-002", RelationshipType = "Primary" }
            },
            ApplicationBusinessProcessLinks =
            {
                new ApplicationBusinessProcessLink { Id = "PROCAPP-001", CompanyId = "CO-001", ApplicationId = "APP-002", BusinessProcessId = "PROC-002", RelationshipType = "Primary", IsPrimary = true }
            },
            ApplicationCounterpartyLinks =
            {
                new ApplicationCounterpartyLink { Id = "APPORG-001", CompanyId = "CO-001", ApplicationId = "APP-002", ExternalOrganizationId = "ORG-002", RelationshipType = "VendorProvided", IntegrationType = "B2B" }
            },
            BusinessProcessCounterpartyLinks =
            {
                new BusinessProcessCounterpartyLink { Id = "PROCORG-001", CompanyId = "CO-001", BusinessProcessId = "PROC-002", ExternalOrganizationId = "ORG-002", RelationshipType = "Supplier", IsPrimary = true }
            },
            CrossTenantAccessPolicies =
            {
                new CrossTenantAccessPolicyRecord { Id = "POL-001", CompanyId = "CO-001", ExternalOrganizationId = "ORG-001", ResourceTenantDomain = "contoso.com", HomeTenantDomain = "northwind.example", RelationshipType = "Vendor", PolicyName = "Vendor Access", AccessDirection = "Inbound" },
                new CrossTenantAccessPolicyRecord { Id = "POL-002", CompanyId = "CO-001", ExternalOrganizationId = "ORG-002", ResourceTenantDomain = "contoso.com", HomeTenantDomain = "northwind.example", RelationshipType = "Vendor", PolicyName = "Vendor Access", AccessDirection = "Inbound" }
            },
            CrossTenantAccessEvents =
            {
                new CrossTenantAccessEvent { Id = "EVENT-001", CompanyId = "CO-001", AccountId = "ACT-001", ExternalOrganizationId = "ORG-002", PolicyId = "POL-002", EventType = "InvitationRedeemed", EventCategory = "Lifecycle", EventStatus = "Success", SourceSystem = "EntraID" }
            },
            ObservedEntitySnapshots =
            {
                new ObservedEntitySnapshot { Id = "OBS-APP", CompanyId = "CO-001", SourceSystem = "CMDB", EntityType = "Application", EntityId = "APP-002", DisplayName = "Portal", ObservedState = "Active", GroundTruthState = "Active" },
                new ObservedEntitySnapshot { Id = "OBS-SVC", CompanyId = "CO-001", SourceSystem = "APM", EntityType = "ApplicationService", EntityId = "SVC-002", DisplayName = "Portal API", ObservedState = "Healthy", GroundTruthState = "Healthy" },
                new ObservedEntitySnapshot { Id = "OBS-TEN", CompanyId = "CO-001", SourceSystem = "EntraID", EntityType = "CloudTenant", EntityId = "TEN-002", DisplayName = "Contoso Productivity", ObservedState = "Managed", GroundTruthState = "Managed" },
                new ObservedEntitySnapshot { Id = "OBS-GUEST", CompanyId = "CO-001", SourceSystem = "EntraID", EntityType = "Account", EntityId = "ACT-001", DisplayName = "ava.reed_northwind#EXT#@contoso.test", ObservedState = "Enabled", GroundTruthState = "Enabled", OwnerReference = "ORG-002" }
            },
            CollaborationChannelTabs =
            {
                new CollaborationChannelTab { Id = "TAB-001", CompanyId = "CO-001", CollaborationChannelId = "CHAN-001", Name = "Portal", TabType = "Website", TargetType = "Application", TargetId = "APP-002", TargetReference = "app://APP-002" }
            },
            Databases =
            {
                new DatabaseRepository { Id = "DB-001", CompanyId = "CO-001", Name = "PortalDb", Engine = "SQL Server", OwnerDepartmentId = "DEP-001", AssociatedApplicationId = "APP-002" }
            },
            PluginRecords =
            {
                new PluginGeneratedRecord { Id = "PLUGIN-APP", PluginCapability = "Meta", RecordType = "App", AssociatedEntityType = "Application", AssociatedEntityId = "APP-002" },
                new PluginGeneratedRecord { Id = "PLUGIN-SVC", PluginCapability = "Meta", RecordType = "Svc", AssociatedEntityType = "ApplicationService", AssociatedEntityId = "SVC-002" },
                new PluginGeneratedRecord { Id = "PLUGIN-TEN", PluginCapability = "Meta", RecordType = "Tenant", AssociatedEntityType = "CloudTenant", AssociatedEntityId = "TEN-002" },
                new PluginGeneratedRecord { Id = "PLUGIN-ORG", PluginCapability = "Meta", RecordType = "Vendor", AssociatedEntityType = "ExternalOrganization", AssociatedEntityId = "ORG-002" },
                new PluginGeneratedRecord { Id = "PLUGIN-PROC", PluginCapability = "Meta", RecordType = "Process", AssociatedEntityType = "BusinessProcess", AssociatedEntityId = "PROC-002" }
            }
        };

        var service = new WorldOwnershipReconciliationService(new DefaultLayerOwnershipRegistry());

        var result = service.Reconcile(world);

        Assert.True(result.UpdatedCount > 0);
        Assert.True(result.RemovedCount > 0);
        Assert.Single(world.Applications);
        Assert.Single(world.ApplicationServices);
        Assert.Single(world.CloudTenants);
        Assert.Single(world.BusinessProcesses);
        Assert.Single(world.ExternalOrganizations);
        Assert.Contains(world.ApplicationTenantLinks, link => link.ApplicationId == "APP-001" && link.CloudTenantId == "TEN-001");
        Assert.Contains(world.ApplicationBusinessProcessLinks, link => link.ApplicationId == "APP-001" && link.BusinessProcessId == "PROC-001");
        Assert.Contains(world.ApplicationCounterpartyLinks, link => link.ApplicationId == "APP-001" && link.ExternalOrganizationId == "ORG-001");
        Assert.Contains(world.BusinessProcessCounterpartyLinks, link => link.BusinessProcessId == "PROC-001" && link.ExternalOrganizationId == "ORG-001");
        Assert.Contains(world.CrossTenantAccessPolicies, policy => policy.Id == "POL-001");
        Assert.Contains(world.CrossTenantAccessEvents, accessEvent => accessEvent.ExternalOrganizationId == "ORG-001" && accessEvent.PolicyId == "POL-001");
        Assert.Contains(world.People, person => person.EmployerOrganizationId == "ORG-001");
        Assert.Contains(world.Accounts, account => account.InvitedOrganizationId == "ORG-001");
        Assert.Contains(world.Databases, database => database.AssociatedApplicationId == "APP-001");
        Assert.Contains(world.CollaborationChannelTabs, tab => tab.TargetId == "APP-001");
        Assert.Contains(world.ObservedEntitySnapshots, snapshot => snapshot.Id == "OBS-APP" && snapshot.EntityId == "APP-001");
        Assert.Contains(world.ObservedEntitySnapshots, snapshot => snapshot.Id == "OBS-SVC" && snapshot.EntityId == "SVC-001");
        Assert.Contains(world.ObservedEntitySnapshots, snapshot => snapshot.Id == "OBS-TEN" && snapshot.EntityId == "TEN-001");
        Assert.Contains(world.ObservedEntitySnapshots, snapshot => snapshot.Id == "OBS-GUEST" && snapshot.OwnerReference == "ORG-001");
        Assert.Contains(world.PluginRecords, record => record.Id == "PLUGIN-APP" && record.AssociatedEntityId == "APP-001");
        Assert.Contains(world.PluginRecords, record => record.Id == "PLUGIN-SVC" && record.AssociatedEntityId == "SVC-001");
        Assert.Contains(world.PluginRecords, record => record.Id == "PLUGIN-TEN" && record.AssociatedEntityId == "TEN-001");
        Assert.Contains(world.PluginRecords, record => record.Id == "PLUGIN-ORG" && record.AssociatedEntityId == "ORG-001");
        Assert.Contains(world.PluginRecords, record => record.Id == "PLUGIN-PROC" && record.AssociatedEntityId == "PROC-001");
        Assert.NotEmpty(result.Warnings);
    }
}
