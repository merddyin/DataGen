using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.Catalogs;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class ApplicationGenerationTests
{
    [Fact]
    public void WorldGenerator_Populates_Applications_When_Enabled()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 1,
                Scenario = new ScenarioDefinition
                {
                    Name = "Applications Test",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 4,
                        IncludeLineOfBusinessApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Applications Test Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 120,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 1,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet());

        Assert.True(result.World.Applications.Count >= 8, $"Expected richer application coverage, but found only {result.World.Applications.Count} applications.");
        Assert.All(result.World.Applications, application => Assert.False(string.IsNullOrWhiteSpace(application.OwnerDepartmentId)));
        Assert.Contains(result.World.Applications, application => !string.IsNullOrWhiteSpace(application.BusinessCapability));
        Assert.Contains(result.WorldMetadata!.AppliedLayers, layer => layer == "BusinessProcesses");
        Assert.Contains(result.WorldMetadata!.AppliedLayers, layer => layer == "ApplicationTopology");
        Assert.Contains(result.WorldMetadata!.AppliedLayers, layer => layer == "CloudTenancy");
        Assert.Contains(result.WorldMetadata!.AppliedLayers, layer => layer == "Applications");
    }

    [Fact]
    public void WorldGenerator_Scales_Application_Estate_For_Larger_Enterprises()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Large Manufacturer",
                    IndustryProfile = "Manufacturing",
                    GeographyProfile = "Regional-US",
                    EmployeeSize = new SizeBand { Minimum = 1800, Maximum = 2600 },
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 6,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Large Manufacturer",
                            Industry = "Manufacturing",
                            EmployeeCount = 2200,
                            BusinessUnitCount = 4,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 4,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet());

        Assert.True(result.World.Applications.Count >= 28, $"Expected a materially larger enterprise application estate, but found only {result.World.Applications.Count} applications.");
        Assert.NotEmpty(result.World.ApplicationDependencies);
        Assert.NotEmpty(result.World.ApplicationServices);
        Assert.NotEmpty(result.World.ApplicationServiceDependencies);
        Assert.NotEmpty(result.World.ApplicationServiceHostings);
        Assert.NotEmpty(result.World.CloudTenants);
        Assert.NotEmpty(result.World.ApplicationTenantLinks);
        Assert.NotEmpty(result.World.BusinessProcesses);
        Assert.NotEmpty(result.World.ApplicationBusinessProcessLinks);
        Assert.NotEmpty(result.World.ApplicationRepositoryLinks);
        Assert.Contains(result.World.Applications, application => application.Criticality == "High");
        Assert.Contains(result.World.ApplicationDependencies, dependency => dependency.DependencyType == "Identity");
        Assert.Contains(result.World.ApplicationServiceHostings, hosting => hosting.HostType is "Server" or "SaaSPlatform" or "ManagedPlatform");
        Assert.Contains(result.World.CloudTenants, tenant => tenant.Provider == "Microsoft" || tenant.Provider == "Salesforce" || tenant.Provider == "Workday");
        Assert.Contains(result.World.BusinessProcesses, process => process.Name == "Plan to Produce" || process.Name == "Order to Cash");
        Assert.Contains(result.World.ApplicationRepositoryLinks, link => link.RepositoryType == "Database");
    }

    [Fact]
    public void WorldGenerator_Uses_Curated_Application_Template_Catalogs_For_Manufacturing()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var catalogs = new FileSystemCatalogLoader().LoadFromPath(TestEnvironmentPaths.GetCatalogRoot());
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Catalog Manufacturing",
                    IndustryProfile = "Manufacturing",
                    GeographyProfile = "Regional-US",
                    EmployeeSize = new SizeBand { Minimum = 1800, Maximum = 2600 },
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 6,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Catalog Manufacturing",
                            Industry = "Manufacturing",
                            EmployeeCount = 2200,
                            BusinessUnitCount = 4,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 4,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            catalogs);

        Assert.True(
            result.World.Applications.Count >= 120,
            $"Expected a richly populated manufacturing application estate, but found only {result.World.Applications.Count} applications.");
        Assert.Contains(result.World.Applications, application => application.Name == "Workday HCM");
        Assert.Contains(result.World.Applications, application => application.Name == "ServiceNow IT Service Management");
        Assert.Contains(result.World.Applications, application => application.Name == "SAP Extended Warehouse Management");
        Assert.Contains(result.World.Applications, application => application.Name == "Databricks Unity Catalog");
        Assert.DoesNotContain(result.World.Applications, application => application.Name == "Google Chrome");
        Assert.DoesNotContain(result.World.Applications, application => application.Name == "Cisco AnyConnect");
        Assert.DoesNotContain(result.World.Applications, application => application.Name == "OneDrive Sync Client");
        Assert.DoesNotContain(result.World.Applications, application => application.Name == "Windows Server Backup");
        Assert.Contains(result.World.SoftwarePackages, package => package.Name == "Google Chrome");
        Assert.Contains(result.World.SoftwarePackages, package => package.Name == "Cisco AnyConnect");
        Assert.Contains(result.World.SoftwarePackages, package => package.Name == "Windows Server Backup");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Supplier Quality Hub");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Production Planning");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Quality Portal");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Supplier Exchange");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Cash Position Board");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Quote Desk");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Territory Planner");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Web Content Studio");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Escalation Desk");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Release Readiness Board");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Capacity Commit Console");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing ERP Core");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Warehouse Management");
        Assert.Contains(result.World.Applications, application => application.Name == "Catalog Manufacturing Business Continuity Console");
        Assert.Contains(result.World.Applications, application => application.Name.EndsWith("Workplace Coordination", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Applications, application => application.Name.EndsWith("Plant Operations Console", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Applications, application => application.Vendor == "Plex");
        Assert.Contains(result.World.Applications, application => application.Vendor == "PTC");
        Assert.Contains(result.World.Applications, application => application.Vendor == "Infor");
        Assert.Contains(result.World.Applications, application => application.Vendor == "Blue Yonder");
        Assert.Contains(result.World.Applications, application => application.Vendor == "Snowflake");
        Assert.Contains(result.World.Applications, application => application.Vendor == "Workday");
        Assert.Contains(result.World.Applications, application => application.Name == "Workday HCM" && application.Url is not null && application.Url.Contains("workday.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Applications, application => application.Name == "ServiceNow IT Service Management" && application.Url is not null && application.Url.Contains("servicenow.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Applications, application => application.Name == "Siemens Teamcenter" && application.Url is not null && application.Url.Contains("siemens.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Applications, application =>
            application.Name == "Catalog Manufacturing Supplier Quality Hub"
            && string.Equals(application.Vendor, "Catalog Manufacturing", StringComparison.OrdinalIgnoreCase)
            && application.Url is not null
            && application.Url.Contains(".catalogmanufacturing.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.CloudTenants, tenant =>
            tenant.Provider == "Workday"
            && tenant.TenantType == "HumanCapitalManagement"
            && tenant.AuthenticationModel == "Federated"
            && tenant.PrimaryDomain.Contains("workday.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.ApplicationTenantLinks, link =>
            link.RelationshipType == "PrimaryWorkspace"
            && result.World.Applications.Any(application => application.Id == link.ApplicationId && application.Name == "Microsoft Intune Admin Center")
            && result.World.CloudTenants.Any(tenant => tenant.Id == link.CloudTenantId && tenant.Provider == "Microsoft"));
        Assert.Contains(result.World.ApplicationTenantLinks, link =>
            link.RelationshipType == "AnalyticalWorkspace"
            && result.World.Applications.Any(application => application.Id == link.ApplicationId && application.Name == "Databricks Unity Catalog")
            && result.World.CloudTenants.Any(tenant => tenant.Id == link.CloudTenantId && tenant.Provider == "Databricks"));
        Assert.Contains(result.World.Applications, application =>
            application.HostingModel == "SaaS"
            && application.ApplicationType == "SaaS"
            && application.DeploymentType == "Automated");
        Assert.Contains(result.World.Applications, application =>
            application.ApplicationType is "ClientServer" or "PaaS" or "IaaS"
            && application.DeploymentType is "Manual" or "Automated");
        Assert.Contains(result.World.IdentityStores, store =>
            store.StoreType == "EntraTenant"
            && store.Provider == "Microsoft"
            && !store.Name.StartsWith("Catalog Manufacturing", StringComparison.OrdinalIgnoreCase)
            && string.Equals(store.Name, store.PrimaryDomain, StringComparison.OrdinalIgnoreCase)
            && store.PrimaryDomain.EndsWith(".onmicrosoft.com", StringComparison.OrdinalIgnoreCase)
            && !store.PrimaryDomain.Contains(".tenant.onmicrosoft.com", StringComparison.OrdinalIgnoreCase)
            && store.CloudTenantId is not null
            && result.World.CloudTenants.Any(tenant => tenant.Id == store.CloudTenantId));
        Assert.Contains(result.World.Containers, container =>
            container.ContainerType == "CloudTenant"
            && container.CloudTenantId is not null
            && result.World.CloudTenants.Any(tenant => tenant.Id == container.CloudTenantId));
        Assert.Contains(result.World.Containers, container =>
            container.ContainerType == "AdministrativeUnit"
            && container.Platform == "EntraID"
            && container.CloudTenantId is not null
            && !string.IsNullOrWhiteSpace(container.ParentContainerId));
        Assert.Contains(result.World.Containers, container =>
            container.ContainerType == "AzureSubscription"
            && container.Platform == "Azure"
            && container.CloudTenantId is not null
            && !string.IsNullOrWhiteSpace(container.ParentContainerId));
        Assert.Contains(result.World.Containers, container =>
            container.ContainerType == "AzureResourceGroup"
            && container.Platform == "Azure"
            && container.CloudTenantId is not null
            && !string.IsNullOrWhiteSpace(container.ParentContainerId));
        Assert.Contains(result.World.Policies, policy =>
            policy.PolicyType == "IntuneCompliancePolicy"
            && policy.Platform == "Intune"
            && policy.Name == "Windows Device Compliance Baseline");
        Assert.Contains(result.World.Policies, policy =>
            policy.PolicyType == "ConditionalAccessPolicy"
            && policy.Platform == "EntraID"
            && policy.Name == "Require MFA For Administrative Portals");
        Assert.Contains(result.World.Policies, policy =>
            policy.PolicyType == "AzurePolicyDefinition"
            && policy.Platform == "Azure"
            && policy.Name == "Azure Landing Zone Guardrails");
        Assert.Contains(result.World.Policies, policy =>
            policy.PolicyType == "AzurePolicyInitiative"
            && policy.Platform == "Azure"
            && policy.Name.Contains("Tagging Compliance", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.PolicySettings, setting =>
            setting.SettingName == "ScreenLockTimeoutMinutes"
            && setting.IsConflicting);
        Assert.Contains(result.World.PolicySettings, setting =>
            setting.SettingName == "LegacyMonitoringAgentAllowed"
            && setting.IsLegacy);
        Assert.Contains(result.World.PolicyTargetLinks, link =>
            link.TargetType == "IdentityStore"
            && link.AssignmentMode == "Scope"
            && result.World.IdentityStores.Any(store =>
                store.Id == link.TargetId
                && store.StoreType == "EntraTenant"));
        Assert.Contains(result.World.PolicyTargetLinks, link =>
            link.TargetType == "Group"
            && result.World.Groups.Any(group =>
                group.Id == link.TargetId
                && (group.Name == "GG All Employees" || group.Name == "M365 All Employees" || group.Name == "GG Microsoft 365 Users")));
        Assert.Contains(result.World.PolicyTargetLinks, link =>
            link.TargetType == "Container"
            && link.AssignmentMode == "Scope"
            && result.World.Containers.Any(container => container.Id == link.TargetId && container.ContainerType == "AzureSubscription"));
        Assert.Contains(result.World.PolicyTargetLinks, link =>
            link.TargetType == "Container"
            && link.AssignmentMode == "Exemption"
            && link.FilterType == "ExemptionCategory"
            && string.Equals(link.FilterValue, "LegacyMonitoringAgent", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Policy"
            && evidence.RightName == "ConditionalAccessAdministration"
            && evidence.SourceSystem == "EntraID");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "AdministrativeUnitManagement"
            && evidence.SourceSystem == "EntraID");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "IdentityStore"
            && evidence.RightName == "PrivilegedRoleAdministration"
            && evidence.SourceSystem == "EntraID");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "ApplicationAdministration"
            && evidence.SourceSystem == "EntraID");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "HelpdeskAdministration"
            && evidence.SourceSystem == "EntraID");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "IdentityStore"
            && evidence.RightName == "DirectoryReaders"
            && evidence.SourceSystem == "EntraID");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "RoleAssignmentWrite"
            && evidence.SourceSystem == "AzureRBAC");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "ResourceGroupOwner"
            && evidence.SourceSystem == "AzureRBAC");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "ResourceGroupRead"
            && evidence.IsInherited
            && evidence.SourceSystem == "AzureRBAC");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Policy"
            && evidence.RightName == "PolicyAssignmentWrite"
            && evidence.SourceSystem == "AzurePolicy");
        Assert.Contains(result.World.AccessControlEvidence, evidence =>
            evidence.TargetType == "Container"
            && evidence.RightName == "PolicyExemptionWrite"
            && evidence.SourceSystem == "AzurePolicy");
        Assert.Contains(result.World.ApplicationDependencies, dependency =>
            dependency.DependencyType == "UpstreamMasterData"
            && result.World.Applications.Any(application => application.Id == dependency.SourceApplicationId && application.Name == "Workday Recruiting")
            && result.World.Applications.Any(application => application.Id == dependency.TargetApplicationId && application.Name == "Workday HCM"));
        Assert.Contains(result.World.ApplicationDependencies, dependency =>
            dependency.DependencyType == "Identity"
            && dependency.InterfaceType == "GraphApi"
            && result.World.Applications.Any(application => application.Id == dependency.SourceApplicationId && application.Name == "Microsoft Intune Admin Center")
            && result.World.Applications.Any(application => application.Id == dependency.TargetApplicationId && application.Name == "Microsoft Entra Admin Center"));
        Assert.Contains(result.World.ApplicationDependencies, dependency =>
            dependency.DependencyType == "WarehouseExecution"
            && result.World.Applications.Any(application => application.Id == dependency.SourceApplicationId && application.Name == "SAP Extended Warehouse Management")
            && result.World.Applications.Any(application => application.Id == dependency.TargetApplicationId && application.Name == "SAP S/4HANA"));
        Assert.Contains(result.World.ApplicationDependencies, dependency =>
            dependency.DependencyType == "QualityData"
            && result.World.Applications.Any(application => application.Id == dependency.SourceApplicationId && application.Name == "Catalog Manufacturing Supplier Quality Hub")
            && result.World.Applications.Any(application => application.Id == dependency.TargetApplicationId && application.Name == "MasterControl Quality Excellence"));
        Assert.Contains(result.World.ApplicationBusinessProcessLinks, link =>
            link.RelationshipType == "PrimarySystem"
            && link.IsPrimary
            && result.World.BusinessProcesses.Any(process => process.Id == link.BusinessProcessId && process.Name == "Hire to Retire")
            && result.World.Applications.Any(application => application.Id == link.ApplicationId && application.Name == "Workday HCM"));
        Assert.Contains(result.World.ApplicationBusinessProcessLinks, link =>
            link.RelationshipType == "PrimarySystem"
            && link.IsPrimary
            && result.World.BusinessProcesses.Any(process => process.Id == link.BusinessProcessId && process.Name == "Source to Deliver")
            && result.World.Applications.Any(application => application.Id == link.ApplicationId && application.Name == "SAP Extended Warehouse Management"));
        Assert.Contains(result.World.ApplicationBusinessProcessLinks, link =>
            link.RelationshipType == "PrimarySystem"
            && link.IsPrimary
            && result.World.BusinessProcesses.Any(process => process.Id == link.BusinessProcessId && process.Name == "Quality to Release")
            && result.World.Applications.Any(application => application.Id == link.ApplicationId && application.Name == "Catalog Manufacturing Supplier Quality Hub"));
        Assert.Contains(result.World.BusinessProcesses, process => process.Name == "Plan to Produce");
        Assert.Contains(result.World.BusinessProcesses, process => process.Name == "Source to Deliver");
        Assert.Contains(result.World.BusinessProcesses, process => process.Name == "Quality to Release");
        Assert.Contains(result.World.BusinessProcesses, process => process.Name == "Maintain to Operate");
        Assert.Contains(result.World.BusinessProcesses, process => process.Name == "Govern to Comply");
        Assert.Contains(result.World.BusinessProcesses, process => process.Name == "Data to Insight");
        Assert.DoesNotContain(result.World.ApplicationServices, service => service.Name == "Workday HCM Windows Service");
        Assert.Contains(result.World.ApplicationServices, service => service.Name == "Workday HCM Integration Service" && service.Runtime == "saas");
        Assert.DoesNotContain(result.World.ApplicationServices, service => service.Name == "Databricks Lakehouse Platform Web Portal");
        Assert.Contains(result.World.ApplicationServices, service => service.Name == "Databricks Lakehouse Platform Spark Jobs" && service.Runtime == "spark");
        Assert.Contains(result.World.ApplicationServices, service => service.Name == "Catalog Manufacturing Supplier Quality Hub Integration Service" && service.Runtime == "dotnet");
        Assert.Contains(result.World.ApplicationServiceHostings, hosting =>
            hosting.HostType == "Server"
            && string.Equals(hosting.HostingRole, "SQL Server", StringComparison.OrdinalIgnoreCase)
            && result.World.ApplicationServices.Any(service =>
                service.Id == hosting.ApplicationServiceId
                && service.Name == "SAP S/4HANA Database Jobs"));
        Assert.Contains(result.World.ApplicationServiceDependencies, dependency =>
            dependency.DependencyType == "Identity"
            && dependency.InterfaceType == "GraphApi"
            && result.World.ApplicationServices.Any(service =>
                service.Id == dependency.SourceServiceId
                && service.Name == "Microsoft Intune Admin Center API Service")
            && result.World.ApplicationServices.Any(service =>
                service.Id == dependency.TargetServiceId
                && service.Name == "Microsoft Entra Admin Center API Service"));
        Assert.Contains(result.World.ApplicationServiceDependencies, dependency =>
            dependency.DependencyType == "WarehouseExecution"
            && dependency.InterfaceType == "RFC"
            && result.World.ApplicationServices.Any(service =>
                service.Id == dependency.SourceServiceId
                && service.Name == "SAP Extended Warehouse Management Integration Service")
            && result.World.ApplicationServices.Any(service =>
                service.Id == dependency.TargetServiceId
                && service.Name == "SAP S/4HANA API Service"));
        Assert.Contains(result.World.ApplicationServiceDependencies, dependency =>
            dependency.DependencyType == "QualityData"
            && dependency.InterfaceType == "REST"
            && result.World.ApplicationServices.Any(service =>
                service.Id == dependency.SourceServiceId
                && service.Name == "Catalog Manufacturing Supplier Quality Hub Integration Service")
            && result.World.ApplicationServices.Any(service =>
                service.Id == dependency.TargetServiceId
                && service.Name.Contains("MasterControl Quality Excellence", StringComparison.OrdinalIgnoreCase)
                && service.Name.EndsWith("Service", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(result.World.ApplicationServiceHostings, hosting =>
            hosting.HostType == "ManagedPlatform"
            && hosting.HostName == "Databricks SQL Warehouse"
            && hosting.HostingRole == "DataPlane"
            && result.World.ApplicationServices.Any(service =>
                service.Id == hosting.ApplicationServiceId
                && service.Name == "Databricks Lakehouse Platform SQL Warehouse"));
        Assert.Contains(result.World.ApplicationServiceHostings, hosting =>
            hosting.HostType == "SaaSPlatform"
            && hosting.HostName == "Workday Integration Cloud"
            && result.World.ApplicationServices.Any(service =>
                service.Id == hosting.ApplicationServiceId
                && service.Name == "Workday HCM Integration Service"));
        Assert.Contains(result.World.Applications, application =>
            application.Name == "SAP Global Trade Services"
            && result.World.Departments.Any(department =>
                department.Id == application.OwnerDepartmentId
                && (department.Name.Contains("Operations", StringComparison.OrdinalIgnoreCase)
                    || department.Name.Contains("Procurement", StringComparison.OrdinalIgnoreCase)
                    || department.Name.Contains("Finance", StringComparison.OrdinalIgnoreCase))));
        Assert.Contains(result.World.Applications, application =>
            string.Equals(application.Vendor, "Catalog Manufacturing", StringComparison.OrdinalIgnoreCase)
            && application.Url is not null
            && application.Url.Contains(".catalogmanufacturing.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Uses_Vendor_Domains_Without_Tenant_Branded_Apex_Subdomains()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var catalogs = new FileSystemCatalogLoader().LoadFromPath(TestEnvironmentPaths.GetCatalogRoot());
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Vendor URL Realism",
                    IndustryProfile = "Manufacturing",
                    GeographyProfile = "Regional-US",
                    EmployeeSize = new SizeBand { Minimum = 1800, Maximum = 2600 },
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 6,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Vendor URL Realism",
                            Industry = "Manufacturing",
                            EmployeeCount = 2200,
                            BusinessUnitCount = 4,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 4,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            catalogs);

        Assert.Contains(result.World.Applications, application =>
            string.Equals(application.HostingModel, "SaaS", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(application.Vendor)
            && !string.Equals(application.Vendor, "Vendor URL Realism", StringComparison.OrdinalIgnoreCase)
            && application.Url is not null
            && !application.Url.Contains("vendorurlrealism.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.Applications, application =>
            string.Equals(application.HostingModel, "SaaS", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(application.Vendor)
            && !string.Equals(application.Vendor, "Vendor URL Realism", StringComparison.OrdinalIgnoreCase)
            && application.Url is not null
            && application.Url.Contains($".{application.Vendor.ToLowerInvariant().Replace(" ", string.Empty)}.com", StringComparison.OrdinalIgnoreCase)
            && application.Url.Contains("vendorurlrealism", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Falls_Back_To_Vendor_Like_Domains_When_Vendor_Metadata_Is_Missing()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 11,
                Scenario = new ScenarioDefinition
                {
                    Name = "Vendor Domain Fallback Test",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 1,
                        IncludeLineOfBusinessApplications = false,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Vendor Domain Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 120,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["software_catalog"] = new List<Dictionary<string, string?>>
                    {
                        NewApplicationRow(("Name", "Cisco AnyConnect"), ("Category", "VPN"), ("Vendor", "Cisco"), ("Version", "5.1"))
                    },
                    ["essential_software_patterns"] = new List<Dictionary<string, string?>>
                    {
                        NewApplicationRow(("Industry", "All"), ("MinimumEmployees", "1"), ("Priority", "1"), ("Name", "Cisco AnyConnect"), ("Category", "VPN"), ("Vendor", "Cisco"))
                    }
                }
            });

        Assert.DoesNotContain(result.World.Applications, application =>
            application.Name == "Cisco AnyConnect");
        Assert.Contains(result.World.SoftwarePackages, package =>
            package.Name == "Cisco AnyConnect"
            && string.Equals(package.Vendor, "Cisco", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Uses_Compact_Hosts_For_Company_Owned_Applications()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var catalogs = new FileSystemCatalogLoader().LoadFromPath(TestEnvironmentPaths.GetCatalogRoot());
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Catalog Manufacturing",
                    IndustryProfile = "Manufacturing",
                    GeographyProfile = "Regional-US",
                    EmployeeSize = new SizeBand { Minimum = 1800, Maximum = 2600 },
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 6,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Catalog Manufacturing",
                            Industry = "Manufacturing",
                            EmployeeCount = 2200,
                            BusinessUnitCount = 4,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 4,
                            Countries = new() { "United States" }
                        }
                    }
                }
            },
            catalogs);

        Assert.Contains(result.World.Applications, application =>
            application.Name == "Catalog Manufacturing ERP Core"
            && string.Equals(application.Vendor, "Catalog Manufacturing", StringComparison.OrdinalIgnoreCase)
            && string.Equals(application.Url, "https://erpcore.catalogmanufacturing.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.Applications, application =>
            string.Equals(application.Vendor, "Catalog Manufacturing", StringComparison.OrdinalIgnoreCase)
            && application.Url is not null
            && application.Url.Contains("catalogmanufacturingerpcore.catalogmanufacturing.com", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string?> NewApplicationRow(params (string Key, string? Value)[] entries)
    {
        var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            row[key] = value;
        }

        return row;
    }

    [Fact]
    public void WorldGenerator_Can_Model_Acquired_Companies_For_Flagship_Scenarios()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var catalogs = new FileSystemCatalogLoader().LoadFromPath(TestEnvironmentPaths.GetCatalogRoot());
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Acquired Company Test",
                    IndustryProfile = "Manufacturing",
                    GeographyProfile = "North-America",
                    Applications = new ApplicationProfile
                    {
                        IncludeApplications = true,
                        BaseApplicationCount = 8,
                        IncludeLineOfBusinessApplications = true,
                        IncludeSaaSApplications = true
                    },
                    Companies = new()
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Acquired Company Test",
                            Industry = "Manufacturing",
                            EmployeeCount = 1500,
                            BusinessUnitCount = 4,
                            DepartmentCountPerBusinessUnit = 4,
                            TeamCountPerDepartment = 3,
                            OfficeCount = 4,
                            AcquiredCompanyCount = 1,
                            Countries = new() { "United States", "Canada" }
                        }
                    }
                }
            },
            catalogs);

        Assert.Contains(result.World.ExternalOrganizations, organization =>
            organization.RelationshipType == "AcquiredBy"
            && organization.Segment == "AcquiredSubsidiary"
            && organization.RelationshipBasis == "AcquisitionIntegration"
            && organization.RelationshipScope == "Enterprise"
            && !string.IsNullOrWhiteSpace(organization.PrimaryDomain));
    }
}
