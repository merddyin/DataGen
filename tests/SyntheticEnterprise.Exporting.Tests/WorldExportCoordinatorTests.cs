using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Exporting.Profiles;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Services;
using SyntheticEnterprise.Exporting.Writers;
using Xunit;

namespace SyntheticEnterprise.Exporting.Tests;

public sealed class WorldExportCoordinatorTests
{
    [Fact]
    public void Export_Writes_Summary_And_Returns_Manifest()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            var coordinator = new WorldExportCoordinator(
                new EmptyEntityTableProvider(),
                new EmptyLinkTableProvider(),
                new JsonArtifactWriter(),
                new ExportManifestBuilder(),
                new ExportSummaryBuilder(),
                new ExportPathResolver());

            var manifest = coordinator.Export(new { }, new ExportRequest
            {
                Format = ExportSerializationFormat.Json,
                OutputPath = temp,
                IncludeManifest = true,
                IncludeSummary = true
            });

            Assert.NotNull(manifest);
            Assert.Contains(manifest.Artifacts, a => a.ArtifactKind == ExportArtifactKind.Summary);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Export_Writes_Application_Entity_And_Link_Artifacts()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            var coordinator = new WorldExportCoordinator(
                new NormalizedEntityTableProvider(),
                new NormalizedLinkTableProvider(),
                new JsonArtifactWriter(),
                new ExportManifestBuilder(),
                new ExportSummaryBuilder(),
                new ExportPathResolver());

            var manifest = coordinator.Export(
                new GenerationResult
                {
                    World = new SyntheticEnterpriseWorld
                    {
                        Containers =
                        {
                            new EnvironmentContainer
                            {
                                Id = "CNT-001",
                                CompanyId = "CO-001",
                                Name = "contoso.com",
                                ContainerType = "DirectoryDomain",
                                Platform = "ActiveDirectory",
                                ContainerPath = "DC=contoso,DC=com",
                                Purpose = "Directory naming context",
                                Environment = "Production",
                                BlocksPolicyInheritance = true,
                                IdentityStoreId = "IDS-001",
                                SourceEntityType = "IdentityStore",
                                SourceEntityId = "IDS-001"
                            }
                        },
                        Policies =
                        {
                            new PolicyRecord
                            {
                                Id = "POL-001",
                                CompanyId = "CO-001",
                                Name = "Default Domain Policy",
                                PolicyType = "GroupPolicyObject",
                                Platform = "ActiveDirectory",
                                Category = "IdentityBaseline",
                                Environment = "Production",
                                Status = "Enabled",
                                Description = "Baseline domain credential settings.",
                                IdentityStoreId = "IDS-001"
                            }
                        },
                        PolicySettings =
                        {
                            new PolicySettingRecord
                            {
                                Id = "PST-001",
                                CompanyId = "CO-001",
                                PolicyId = "POL-001",
                                SettingName = "MinimumPasswordLength",
                                SettingCategory = "PasswordPolicy",
                                ValueType = "Integer",
                                ConfiguredValue = "14",
                                IsLegacy = false,
                                IsConflicting = false
                            }
                        },
                        PolicyTargetLinks =
                        {
                            new PolicyTargetLink
                            {
                                Id = "PTL-001",
                                CompanyId = "CO-001",
                                PolicyId = "POL-001",
                                TargetType = "Container",
                                TargetId = "CNT-001",
                                AssignmentMode = "Linked",
                                LinkEnabled = true,
                                IsEnforced = true,
                                LinkOrder = 1
                            },
                            new PolicyTargetLink
                            {
                                Id = "PTL-002",
                                CompanyId = "CO-001",
                                PolicyId = "POL-001",
                                TargetType = "Container",
                                TargetId = "CNT-001",
                                AssignmentMode = "WmiFilter",
                                LinkEnabled = true,
                                IsEnforced = false,
                                LinkOrder = 2,
                                FilterType = "WmiQuery",
                                FilterValue = "SELECT * FROM Win32_OperatingSystem WHERE ProductType = 1"
                            },
                            new PolicyTargetLink
                            {
                                Id = "PTL-003",
                                CompanyId = "CO-001",
                                PolicyId = "POL-001",
                                TargetType = "Container",
                                TargetId = "CNT-001",
                                AssignmentMode = "Linked",
                                LinkEnabled = false,
                                IsEnforced = false,
                                LinkOrder = 3
                            }
                        },
                        AccessControlEvidence =
                        {
                            new AccessControlEvidenceRecord
                            {
                                Id = "ACE-001",
                                CompanyId = "CO-001",
                                PrincipalObjectId = "GRP-001",
                                PrincipalType = "Group",
                                TargetType = "Policy",
                                TargetId = "POL-001",
                                RightName = "EditSettings",
                                AccessType = "Allow",
                                IsInherited = false,
                                IsDefaultEntry = false,
                                SourceSystem = "ActiveDirectory",
                                Notes = "Non-default delegated administration"
                            },
                            new AccessControlEvidenceRecord
                            {
                                Id = "ACE-002",
                                CompanyId = "CO-001",
                                PrincipalObjectId = "GRP-002",
                                PrincipalType = "Group",
                                TargetType = "DocumentFolder",
                                TargetId = "FOL-001",
                                RightName = "FolderRead",
                                AccessType = "Deny",
                                IsInherited = true,
                                IsDefaultEntry = false,
                                SourceSystem = "SharePoint",
                                InheritanceSourceId = "LIB-001",
                                Notes = "Unique-permission break with inherited deny context"
                            }
                        },
                        Applications =
                        {
                            new ApplicationRecord
                            {
                                Id = "APP-001",
                                CompanyId = "CO-001",
                                Name = "Contoso ERP",
                                Category = "Operations",
                                Vendor = "Contoso",
                                BusinessCapability = "Enterprise Resource Planning",
                                HostingModel = "Hybrid",
                                Environment = "Production",
                                Criticality = "High",
                                DataSensitivity = "Confidential",
                                UserScope = "Enterprise",
                                OwnerDepartmentId = "DEP-001",
                                Url = "https://contoso-erp.contoso.apps.test",
                                SsoEnabled = true,
                                MfaRequired = true
                            },
                            new ApplicationRecord
                            {
                                Id = "APP-002",
                                CompanyId = "CO-001",
                                Name = "Contoso Identity Portal",
                                Category = "Security",
                                Vendor = "Contoso",
                                BusinessCapability = "Identity and Access",
                                HostingModel = "Hybrid",
                                Environment = "Production",
                                Criticality = "High",
                                DataSensitivity = "Restricted",
                                UserScope = "Enterprise",
                                OwnerDepartmentId = "DEP-002",
                                Url = "https://contoso-identity.contoso.apps.test",
                                SsoEnabled = true,
                                MfaRequired = true
                            }
                        },
                        ApplicationServices =
                        {
                            new ApplicationService
                            {
                                Id = "APPSVC-001",
                                CompanyId = "CO-001",
                                ApplicationId = "APP-001",
                                Name = "Contoso ERP API",
                                ServiceType = "API",
                                Runtime = "dotnet",
                                DeploymentModel = "VirtualMachine",
                                Environment = "Production",
                                OwnerTeamId = "TEAM-001",
                                Criticality = "High"
                            },
                            new ApplicationService
                            {
                                Id = "APPSVC-002",
                                CompanyId = "CO-001",
                                ApplicationId = "APP-002",
                                Name = "Contoso Identity API",
                                ServiceType = "API",
                                Runtime = "dotnet",
                                DeploymentModel = "VirtualMachine",
                                Environment = "Production",
                                OwnerTeamId = "TEAM-002",
                                Criticality = "High"
                            }
                        },
                        ApplicationDependencies =
                        {
                            new ApplicationDependency
                            {
                                Id = "APPDEP-001",
                                CompanyId = "CO-001",
                                SourceApplicationId = "APP-001",
                                TargetApplicationId = "APP-002",
                                DependencyType = "Identity",
                                InterfaceType = "SSO",
                                Criticality = "High"
                            }
                        },
                        ApplicationRepositoryLinks =
                        {
                            new ApplicationRepositoryLink
                            {
                                Id = "ARL-001",
                                CompanyId = "CO-001",
                                ApplicationId = "APP-001",
                                RepositoryId = "DB-001",
                                RepositoryType = "Database",
                                RelationshipType = "PrimaryDataStore",
                                Criticality = "High"
                            }
                        },
                        ApplicationServiceDependencies =
                        {
                            new ApplicationServiceDependency
                            {
                                Id = "APPSD-001",
                                CompanyId = "CO-001",
                                SourceServiceId = "APPSVC-001",
                                TargetServiceId = "APPSVC-002",
                                DependencyType = "Identity",
                                InterfaceType = "HTTPS",
                                Criticality = "High"
                            }
                        },
                        ApplicationServiceHostings =
                        {
                            new ApplicationServiceHosting
                            {
                                Id = "APPHST-001",
                                CompanyId = "CO-001",
                                ApplicationServiceId = "APPSVC-001",
                                HostType = "Server",
                                HostId = "SRV-001",
                                HostName = "SRV-CONTOSO-APP-001",
                                HostingRole = "Application Server",
                                DeploymentModel = "VirtualMachine"
                            }
                        },
                        IdentityStores =
                        {
                            new IdentityStore
                            {
                                Id = "IDS-001",
                                CompanyId = "CO-001",
                                Name = "Contoso Active Directory",
                                StoreType = "ActiveDirectoryDomain",
                                Provider = "Microsoft",
                                PrimaryDomain = "contoso.com",
                                NamingContext = "DC=contoso,DC=com",
                                DirectoryMode = "HybridDirectory",
                                AuthenticationModel = "Kerberos",
                                Environment = "Production",
                                IsPrimary = true
                            }
                        },
                        CloudTenants =
                        {
                            new CloudTenant
                            {
                                Id = "TEN-001",
                                CompanyId = "CO-001",
                                Provider = "Microsoft",
                                TenantType = "ProductivitySuite",
                                Name = "Contoso Microsoft ProductivitySuite",
                                PrimaryDomain = "contoso-microsoft.tenant.onmicrosoft.com",
                                Region = "North America",
                                AuthenticationModel = "Federated",
                                Environment = "Production",
                                AdminDepartmentId = "DEP-002"
                            }
                        },
                        ApplicationTenantLinks =
                        {
                            new ApplicationTenantLink
                            {
                                Id = "APPTEN-001",
                                CompanyId = "CO-001",
                                ApplicationId = "APP-002",
                                CloudTenantId = "TEN-001",
                                RelationshipType = "IdentityControlPlane",
                                IsPrimary = true
                            }
                        },
                        BusinessProcesses =
                        {
                            new BusinessProcess
                            {
                                Id = "PROC-001",
                                CompanyId = "CO-001",
                                Name = "Order to Cash",
                                Domain = "Revenue",
                                BusinessCapability = "Revenue Operations",
                                OwnerDepartmentId = "DEP-001",
                                OperatingModel = "Regional",
                                ProcessScope = "Enterprise",
                                Criticality = "High",
                                CustomerFacing = true
                            }
                        },
                        ApplicationBusinessProcessLinks =
                        {
                            new ApplicationBusinessProcessLink
                            {
                                Id = "APPPROC-001",
                                CompanyId = "CO-001",
                                ApplicationId = "APP-001",
                                BusinessProcessId = "PROC-001",
                                RelationshipType = "PrimarySystem",
                                IsPrimary = true
                            }
                        },
                        ExternalOrganizations =
                        {
                            new ExternalOrganization
                            {
                                Id = "EXT-001",
                                CompanyId = "CO-001",
                                Name = "Northwind Distribution",
                                LegalName = "Northwind Distribution LLC",
                                Description = "Regional customer operating in industrial distribution.",
                                Tagline = "Deliver measurable outcomes",
                                RelationshipType = "Customer",
                                Industry = "Manufacturing",
                                Country = "United States",
                                PrimaryDomain = "northwinddistribution.com",
                                Website = "https://northwinddistribution.example.test",
                                ContactEmail = "sales@northwinddistribution.com",
                                TaxIdentifier = "12-3456789",
                                Segment = "StrategicAccount",
                                RevenueBand = "Enterprise",
                                OwnerDepartmentId = "DEP-001",
                                Criticality = "High"
                            }
                        },
                        CrossTenantAccessPolicies =
                        {
                            new CrossTenantAccessPolicyRecord
                            {
                                Id = "XTP-001",
                                CompanyId = "CO-001",
                                ExternalOrganizationId = "EXT-001",
                                ResourceTenantDomain = "contoso.test",
                                HomeTenantDomain = "northwinddistribution.example.test",
                                RelationshipType = "Customer",
                                PolicyName = "Northwind Distribution Cross-Tenant Access",
                                AccessDirection = "Inbound",
                                TrustLevel = "StandardTrust",
                                DefaultAccess = "ScopedAllow",
                                ConditionalAccessProfile = "GuestCollaborationControls",
                                AllowedResourceScope = "PartnerCollaborationAndLOBApps",
                                B2BCollaborationEnabled = true,
                                InboundTrustMfa = true,
                                InboundTrustCompliantDevice = false,
                                AllowInvitations = true,
                                EntitlementManagementEnabled = true
                            }
                        },
                        CrossTenantAccessEvents =
                        {
                            new CrossTenantAccessEvent
                            {
                                Id = "XTE-001",
                                CompanyId = "CO-001",
                                AccountId = "ACT-001",
                                ExternalOrganizationId = "EXT-001",
                                EventType = "InvitationSent",
                                EventStatus = "Completed",
                                EventCategory = "Invitation",
                                ActorAccountId = "ACT-001",
                                PolicyId = "XTP-001",
                                ResourceReference = "john.doe@contoso.test",
                                EntitlementPackageName = "Partner Collaboration Access",
                                ReviewDecision = "Approved",
                                SourceSystem = "Entra ID",
                                EventAt = DateTimeOffset.Parse("2026-04-09T12:00:00Z")
                            }
                        },
                        ObservedEntitySnapshots =
                        {
                            new ObservedEntitySnapshot
                            {
                                Id = "OBS-001",
                                CompanyId = "CO-001",
                                SourceSystem = "Entra ID",
                                EntityType = "Account",
                                EntityId = "ACT-001",
                                DisplayName = "john.doe@contoso.test",
                                ObservedState = "Enabled/MfaUnknown",
                                GroundTruthState = "Enabled/MfaEnabled",
                                DriftType = "IdentityStateLag",
                                OwnerReference = "ACT-010",
                                RecordedAt = DateTimeOffset.Parse("2026-04-10T01:00:00Z")
                            }
                        },
                        ApplicationCounterpartyLinks =
                        {
                            new ApplicationCounterpartyLink
                            {
                                Id = "APPEXT-001",
                                CompanyId = "CO-001",
                                ApplicationId = "APP-001",
                                ExternalOrganizationId = "EXT-001",
                                RelationshipType = "CustomerIntegration",
                                IntegrationType = "PortalOrEDI",
                                Criticality = "High"
                            }
                        },
                        BusinessProcessCounterpartyLinks =
                        {
                            new BusinessProcessCounterpartyLink
                            {
                                Id = "PROCEXT-001",
                                CompanyId = "CO-001",
                                BusinessProcessId = "PROC-001",
                                ExternalOrganizationId = "EXT-001",
                                RelationshipType = "Customer",
                                IsPrimary = true
                            }
                        },
                        OrganizationalUnits =
                        {
                            new DirectoryOrganizationalUnit
                            {
                                Id = "OU-001",
                                CompanyId = "CO-001",
                                Name = "Users",
                                DistinguishedName = "OU=Users,OU=Corp,DC=contoso,DC=test",
                                Purpose = "User Accounts"
                            }
                        },
                        Accounts =
                        {
                            new DirectoryAccount
                            {
                                Id = "ACT-001",
                                CompanyId = "CO-001",
                                PersonId = "PER-001",
                                AccountType = "User",
                                SamAccountName = "jdoe001",
                                UserPrincipalName = "john.doe@contoso.test",
                                Mail = "john.doe@contoso.test",
                                DistinguishedName = "CN=John Doe,OU=Users,OU=Corp,DC=contoso,DC=test",
                                OuId = "OU-001",
                                Enabled = true,
                                Privileged = false,
                                MfaEnabled = true,
                                EmployeeId = "EMP-001",
                                GeneratedPassword = "Abc!2345Def$",
                                PasswordProfile = "EmployeeStandard",
                                PasswordLastSet = DateTimeOffset.Parse("2026-04-01T00:00:00Z"),
                                PasswordExpires = DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
                                PasswordNeverExpires = false,
                                MustChangePasswordAtNextLogon = false
                            }
                        },
                        Groups =
                        {
                            new DirectoryGroup
                            {
                                Id = "GRP-001",
                                CompanyId = "CO-001",
                                Name = "SG-AllEmployees",
                                GroupType = "Security",
                                Scope = "Global",
                                MailEnabled = false,
                                DistinguishedName = "CN=SG-AllEmployees,OU=Groups,OU=Corp,DC=contoso,DC=test",
                                OuId = "OU-002",
                                Purpose = "All employees"
                            }
                        },
                        GroupMemberships =
                        {
                            new DirectoryGroupMembership
                            {
                                Id = "MEM-001",
                                GroupId = "GRP-001",
                                MemberObjectId = "ACT-001",
                                MemberObjectType = "Account"
                            }
                        },
                        Devices =
                        {
                            new ManagedDevice
                            {
                                Id = "DEV-001",
                                CompanyId = "CO-001",
                                DeviceType = "Workstation",
                                Hostname = "WS-CONTOSO-001",
                                AssetTag = "AT-CON-1001",
                                SerialNumber = "SN0001001",
                                Manufacturer = "Dell",
                                Model = "Latitude 7450",
                                OperatingSystem = "Windows 11 Enterprise",
                                OperatingSystemVersion = "23H2",
                                AssignedPersonId = "PER-001",
                                AssignedOfficeId = "OFF-001",
                                DirectoryAccountId = "ACT-001",
                                OuId = "OU-003",
                                DistinguishedName = "CN=WS-CONTOSO-001,OU=Workstations,OU=Computers,OU=Corp,DC=contoso,DC=test",
                                DomainJoined = true,
                                ComplianceState = "Compliant",
                                LastSeen = DateTimeOffset.Parse("2026-04-09T00:00:00Z")
                            }
                        },
                        Servers =
                        {
                            new ServerAsset
                            {
                                Id = "SRV-001",
                                CompanyId = "CO-001",
                                Hostname = "SRV-CONTOSO-APP-001",
                                ServerRole = "Application Server",
                                Environment = "Production",
                                OperatingSystem = "Windows Server",
                                OperatingSystemVersion = "2022",
                                OfficeId = "OFF-001",
                                OuId = "OU-004",
                                DistinguishedName = "CN=SRV-CONTOSO-APP-001,OU=Production,OU=Servers,OU=Computers,OU=Corp,DC=contoso,DC=test",
                                DomainJoined = true,
                                OwnerTeamId = "TEAM-001",
                                Criticality = "High"
                            }
                        },
                        SoftwarePackages =
                        {
                            new SoftwarePackage
                            {
                                Id = "SW-001",
                                Name = "Microsoft Defender for Endpoint",
                                Category = "Security",
                                Vendor = "Microsoft",
                                Version = "2026.4"
                            },
                            new SoftwarePackage
                            {
                                Id = "SW-002",
                                Name = "CrowdStrike Falcon Sensor",
                                Category = "Security",
                                Vendor = "CrowdStrike",
                                Version = "7.10"
                            }
                        },
                        DeviceSoftwareInstallations =
                        {
                            new DeviceSoftwareInstallation
                            {
                                Id = "DSI-001",
                                DeviceId = "DEV-001",
                                SoftwareId = "SW-001"
                            }
                        },
                        ServerSoftwareInstallations =
                        {
                            new ServerSoftwareInstallation
                            {
                                Id = "SSI-001",
                                ServerId = "SRV-001",
                                SoftwareId = "SW-002"
                            }
                        },
                        EndpointAdministrativeAssignments =
                        {
                            new EndpointAdministrativeAssignment
                            {
                                Id = "EAA-001",
                                CompanyId = "CO-001",
                                EndpointType = "Device",
                                EndpointId = "DEV-001",
                                PrincipalObjectId = "GRP-001",
                                PrincipalType = "Group",
                                AccessRole = "LocalAdministrator",
                                AdministrativeTier = "Tier1",
                                AssignmentScope = "Persistent",
                                ManagementPlane = "DirectoryPolicy"
                            }
                        },
                        EndpointPolicyBaselines =
                        {
                            new EndpointPolicyBaseline
                            {
                                Id = "EPB-001",
                                CompanyId = "CO-001",
                                EndpointType = "Device",
                                EndpointId = "DEV-001",
                                PolicyName = "BitLocker Disk Encryption",
                                PolicyCategory = "Encryption",
                                AssignedFrom = "Intune",
                                EnforcementMode = "Enforced",
                                DesiredState = "Enabled",
                                CurrentState = "Enabled",
                                AdministrativeTier = "Tier2"
                            }
                        },
                        EndpointLocalGroupMembers =
                        {
                            new EndpointLocalGroupMember
                            {
                                Id = "ELG-001",
                                CompanyId = "CO-001",
                                EndpointType = "Device",
                                EndpointId = "DEV-001",
                                LocalGroupName = "Administrators",
                                PrincipalObjectId = "GRP-001",
                                PrincipalType = "Group",
                                PrincipalName = "SG-AllEmployees",
                                MembershipSource = "DirectoryPolicy",
                                AdministrativeTier = "Tier1"
                            }
                        },
                        Databases =
                        {
                            new DatabaseRepository
                            {
                                Id = "DB-001",
                                CompanyId = "CO-001",
                                Name = "ERP_FIN_01",
                                Engine = "SQL Server",
                                Environment = "Production",
                                SizeGb = "240",
                                OwnerDepartmentId = "DEP-001",
                                AssociatedApplicationId = "APP-001",
                                HostServerId = "SRV-001",
                                Sensitivity = "Confidential"
                            }
                        },
                        FileShares =
                        {
                            new FileShareRepository
                            {
                                Id = "FS-001",
                                CompanyId = "CO-001",
                                ShareName = "finance-share-01",
                                UncPath = "\\\\files.contoso.test\\finance-share-01",
                                OwnerDepartmentId = "DEP-001",
                                OwnerPersonId = null,
                                HostServerId = "SRV-002",
                                SharePurpose = "Department",
                                FileCount = "1250",
                                FolderCount = "80",
                                TotalSizeGb = "95",
                                AccessModel = "GroupBased",
                                Sensitivity = "Confidential"
                            }
                        },
                        CollaborationSites =
                        {
                            new CollaborationSite
                            {
                                Id = "SITE-001",
                                CompanyId = "CO-001",
                                Platform = "Teams",
                                Name = "Finance Operations",
                                Url = "https://collab.contoso.test/sites/finance-operations",
                                OwnerPersonId = "PER-001",
                                OwnerDepartmentId = "DEP-001",
                                MemberCount = "44",
                                FileCount = "660",
                                TotalSizeGb = "24",
                                PrivacyType = "Private",
                                WorkspaceType = "Team"
                            }
                        },
                        CollaborationChannels =
                        {
                            new CollaborationChannel
                            {
                                Id = "CHAN-001",
                                CompanyId = "CO-001",
                                CollaborationSiteId = "SITE-001",
                                Name = "General",
                                ChannelType = "Standard",
                                MemberCount = "44",
                                MessageCount = "1800",
                                FileCount = "140"
                            }
                        },
                        DocumentLibraries =
                        {
                            new DocumentLibrary
                            {
                                Id = "LIB-001",
                                CompanyId = "CO-001",
                                CollaborationSiteId = "SITE-001",
                                Name = "Documents",
                                TemplateType = "Documents",
                                ItemCount = "420",
                                TotalSizeGb = "12",
                                Sensitivity = "Confidential"
                            }
                        },
                        RepositoryAccessGrants =
                        {
                            new RepositoryAccessGrant
                            {
                                Id = "RAG-001",
                                RepositoryId = "FS-001",
                                RepositoryType = "FileShare",
                                PrincipalObjectId = "GRP-001",
                                PrincipalType = "Group",
                                AccessLevel = "Modify"
                            }
                        }
                    },
                    Statistics = new GenerationStatistics()
                },
                new ExportRequest
                {
                    Format = ExportSerializationFormat.Json,
                    OutputPath = temp,
                    IncludeManifest = true,
                    IncludeSummary = true
                });

            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "companies");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "offices");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "applications");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "organizational_units");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "accounts");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "groups");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "devices");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "servers");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "software_packages");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "endpoint_administrative_assignments");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "endpoint_policy_baselines");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "endpoint_local_group_members");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "application_services");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "containers");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "identity_stores");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "policies");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "policy_settings");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "policy_target_links");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "access_control_evidence");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "cloud_tenants");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "business_processes");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "external_organizations");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "cross_tenant_access_policies");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "cross_tenant_access_events");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "observed_entity_snapshots");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "databases");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "file_shares");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "collaboration_sites");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "collaboration_channels");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "document_libraries");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "application_dependencies");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "application_repository_links");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "application_service_dependencies");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "application_service_hostings");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "device_software_installations");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "server_software_installations");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "application_tenant_links");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "application_business_process_links");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "application_counterparty_links");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "business_process_counterparty_links");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "group_memberships");
            Assert.Contains(manifest.Artifacts, a => a.LogicalName == "repository_access_grants");
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "companies.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "offices.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "applications.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "organizational_units.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "accounts.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "groups.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "containers.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "devices.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "servers.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "software_packages.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "endpoint_administrative_assignments.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "endpoint_policy_baselines.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "endpoint_local_group_members.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "application_services.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "identity_stores.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "policies.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "policy_settings.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "access_control_evidence.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "cloud_tenants.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "business_processes.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "external_organizations.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "cross_tenant_access_policies.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "cross_tenant_access_events.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "observed_entity_snapshots.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "databases.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "file_shares.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "collaboration_sites.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "collaboration_channels.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "document_libraries.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "application_dependencies.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "application_repository_links.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "application_service_dependencies.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "application_service_hostings.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "device_software_installations.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "server_software_installations.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "application_tenant_links.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "application_business_process_links.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "application_counterparty_links.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "business_process_counterparty_links.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "policy_target_links.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "group_memberships.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "repository_access_grants.json")));

            var accountsJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "accounts.json"));
            var containersJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "containers.json"));
            var identityStoresJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "identity_stores.json"));
            var policiesJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "policies.json"));
            var policySettingsJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "policy_settings.json"));
            var accessControlJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "access_control_evidence.json"));
            var crossTenantPoliciesJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "cross_tenant_access_policies.json"));
            var crossTenantEventsJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "cross_tenant_access_events.json"));
            var softwarePackagesJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "software_packages.json"));
            var deviceSoftwareJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "links", "device_software_installations.json"));
            var policyTargetsJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "links", "policy_target_links.json"));

            Assert.Contains("entitlement_package_name", accountsJson);
            Assert.Contains("DirectoryDomain", containersJson);
            Assert.Contains("blocks_policy_inheritance", containersJson);
            Assert.Contains("Contoso Active Directory", identityStoresJson);
            Assert.Contains("Default Domain Policy", policiesJson);
            Assert.Contains("MinimumPasswordLength", policySettingsJson);
            Assert.Contains("Linked", policyTargetsJson);
            Assert.Contains("WmiQuery", policyTargetsJson);
            Assert.Contains("false", policyTargetsJson);
            Assert.Contains("EditSettings", accessControlJson);
            Assert.Contains("Deny", accessControlJson);
            Assert.Contains("inheritance_source_id", accessControlJson);
            Assert.Contains("LIB-001", accessControlJson);
            Assert.Contains("previous_invited_by_account_id", accountsJson);
            Assert.Contains("allowed_resource_scope", crossTenantPoliciesJson);
            Assert.Contains("event_category", crossTenantEventsJson);
            Assert.Contains("Microsoft Defender for Endpoint", softwarePackagesJson);
            Assert.Contains("DEV-001", deviceSoftwareJson);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Export_Writes_Cmdb_Artifacts()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            var coordinator = new WorldExportCoordinator(
                new NormalizedEntityTableProvider(),
                new NormalizedLinkTableProvider(),
                new JsonArtifactWriter(),
                new ExportManifestBuilder(),
                new ExportSummaryBuilder(),
                new ExportPathResolver());

            var manifest = coordinator.Export(
                new GenerationResult
                {
                    World = new SyntheticEnterpriseWorld
                    {
                        ConfigurationItems =
                        {
                            new ConfigurationItem
                            {
                                Id = "CI-001",
                                CompanyId = "CO-001",
                                CiKey = "application:APP-001",
                                Name = "Contoso ERP",
                                DisplayName = "Contoso ERP",
                                CiType = "Application",
                                CiClass = "HybridApplication",
                                SourceEntityType = "Application",
                                SourceEntityId = "APP-001",
                                Manufacturer = "Contoso",
                                Vendor = "Contoso",
                                Environment = "Production",
                                OperationalStatus = "Active",
                                LifecycleStatus = "InService",
                                BusinessOwnerPersonId = "PER-001",
                                TechnicalOwnerPersonId = "PER-002",
                                SupportTeamId = "TEAM-001",
                                OwningDepartmentId = "DEP-001",
                                OwningLobId = "BU-001",
                                ServiceTier = "Tier1",
                                ServiceClassification = "BusinessApplication",
                                BusinessCriticality = "High",
                                DataSensitivity = "Confidential",
                                MaintenanceWindow = new MaintenanceWindowDefinition
                                {
                                    DayOfWeek = "Saturday",
                                    StartTimeLocal = "20:00",
                                    DurationMinutes = 120,
                                    TimeZone = "America/Chicago",
                                    Frequency = "Weekly"
                                }
                            }
                        },
                        CmdbSourceRecords =
                        {
                            new CmdbSourceRecord
                            {
                                Id = "CMS-001",
                                CompanyId = "CO-001",
                                SourceSystem = "CMDB",
                                SourceRecordId = "CMDB-APP-001",
                                CiType = "Application",
                                CiClass = "HybridApplication",
                                Name = "Contoso ERP",
                                DisplayName = "Contoso ERP",
                                ObservedBusinessOwner = "Jane Owner",
                                ObservedTechnicalOwner = "John Engineer",
                                ObservedServiceTier = "Tier1",
                                ObservedServiceClassification = "BusinessApplication",
                                ObservedBusinessCriticality = "High",
                                ObservedMaintenanceWindow = "Saturday 20:00 (120m America/Chicago)",
                                MatchStatus = "Matched",
                                Confidence = "Medium",
                                LastSeen = DateTimeOffset.Parse("2026-04-12T12:00:00Z"),
                                LastImported = DateTimeOffset.Parse("2026-04-13T12:00:00Z")
                            }
                        },
                        ConfigurationItemRelationships =
                        {
                            new ConfigurationItemRelationship
                            {
                                Id = "CIR-001",
                                CompanyId = "CO-001",
                                SourceConfigurationItemId = "CI-001",
                                TargetConfigurationItemId = "CI-002",
                                RelationshipType = "DependsOn",
                                IsPrimary = false,
                                Confidence = "High",
                                SourceEvidence = "HTTPS",
                                Notes = "Identity"
                            }
                        },
                        CmdbSourceLinks =
                        {
                            new CmdbSourceLink
                            {
                                Id = "CMSL-001",
                                CompanyId = "CO-001",
                                SourceRecordId = "CMS-001",
                                ConfigurationItemId = "CI-001",
                                LinkType = "Matched",
                                MatchMethod = "SyntheticProjection",
                                Confidence = "Medium"
                            }
                        },
                        CmdbSourceRelationships =
                        {
                            new CmdbSourceRelationship
                            {
                                Id = "CMSR-001",
                                CompanyId = "CO-001",
                                SourceSystem = "CMDB",
                                SourceRelationshipId = "CMDB-REL-001",
                                SourceRecordId = "CMS-001",
                                TargetRecordId = "CMS-002",
                                RelationshipType = "DependsOn",
                                IsPrimary = false,
                                Confidence = "Medium",
                                Status = "Active"
                            }
                        }
                    },
                    Statistics = new GenerationStatistics()
                },
                new ExportRequest
                {
                    Format = ExportSerializationFormat.Json,
                    OutputPath = temp
                });

            Assert.Contains(manifest.Artifacts, artifact => artifact.LogicalName == "configuration_items");
            Assert.Contains(manifest.Artifacts, artifact => artifact.LogicalName == "cmdb_source_records");
            Assert.Contains(manifest.Artifacts, artifact => artifact.LogicalName == "configuration_item_relationships");
            Assert.Contains(manifest.Artifacts, artifact => artifact.LogicalName == "cmdb_source_links");
            Assert.Contains(manifest.Artifacts, artifact => artifact.LogicalName == "cmdb_source_relationships");
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "configuration_items.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "entities", "cmdb_source_records.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "configuration_item_relationships.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "cmdb_source_links.json")));
            Assert.True(File.Exists(Path.Combine(manifest.OutputPath, "links", "cmdb_source_relationships.json")));

            var configurationItemsJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "configuration_items.json"));
            var sourceRecordsJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "entities", "cmdb_source_records.json"));
            var sourceLinksJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "links", "cmdb_source_links.json"));
            var sourceRelationshipsJson = File.ReadAllText(Path.Combine(manifest.OutputPath, "links", "cmdb_source_relationships.json"));

            Assert.Contains("maintenance_window_day", configurationItemsJson);
            Assert.Contains("Tier1", configurationItemsJson);
            Assert.Contains("observed_business_owner", sourceRecordsJson);
            Assert.Contains("SyntheticProjection", sourceLinksJson);
            Assert.Contains("CMDB-REL-001", sourceRelationshipsJson);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Export_Masks_Generated_Passwords_By_Default_And_Can_Include_Them_Explicitly()
    {
        var tempMasked = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tempIncluded = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempMasked);
        Directory.CreateDirectory(tempIncluded);

        try
        {
            var coordinator = new WorldExportCoordinator(
                new NormalizedEntityTableProvider(),
                new NormalizedLinkTableProvider(),
                new JsonArtifactWriter(),
                new ExportManifestBuilder(),
                new ExportSummaryBuilder(),
                new ExportPathResolver());

            var result = new GenerationResult
            {
                World = new SyntheticEnterpriseWorld
                {
                    Accounts =
                    {
                        new DirectoryAccount
                        {
                            Id = "ACT-001",
                            CompanyId = "CO-001",
                            AccountType = "User",
                            SamAccountName = "jdoe001",
                            UserPrincipalName = "john.doe@contoso.test",
                            DistinguishedName = "CN=John Doe,OU=Users,OU=Corp,DC=contoso,DC=test",
                            OuId = "OU-001",
                            Enabled = true,
                            MfaEnabled = true,
                            GeneratedPassword = "Abc!2345Def$"
                        }
                    }
                },
                Statistics = new GenerationStatistics()
            };

            var maskedManifest = coordinator.Export(result, new ExportRequest
            {
                Format = ExportSerializationFormat.Json,
                OutputPath = tempMasked,
                CredentialExportMode = CredentialExportMode.Masked
            });

            var includedManifest = coordinator.Export(result, new ExportRequest
            {
                Format = ExportSerializationFormat.Json,
                OutputPath = tempIncluded,
                CredentialExportMode = CredentialExportMode.IncludeGenerated
            });

            var maskedJson = File.ReadAllText(Path.Combine(maskedManifest.OutputPath, "entities", "accounts.json"));
            var includedJson = File.ReadAllText(Path.Combine(includedManifest.OutputPath, "entities", "accounts.json"));

            Assert.Contains("[MASKED:12]", maskedJson);
            Assert.DoesNotContain("Abc!2345Def$", maskedJson);
            Assert.Contains("Abc!2345Def$", includedJson);
        }
        finally
        {
            Directory.Delete(tempMasked, true);
            Directory.Delete(tempIncluded, true);
        }
    }

    private sealed class EmptyEntityTableProvider : IEntityTableProvider
    {
        public IReadOnlyList<object> GetDescriptors() => [];
    }

    private sealed class EmptyLinkTableProvider : ILinkTableProvider
    {
        public IReadOnlyList<object> GetDescriptors() => [];
    }

    [Fact]
    public void Export_Writes_Collaboration_Topology_Artifacts()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var outputRoot = Path.Combine(temp, "topology");
        Directory.CreateDirectory(temp);

        try
        {
            var coordinator = new WorldExportCoordinator(
                new NormalizedEntityTableProvider(),
                new NormalizedLinkTableProvider(),
                new JsonArtifactWriter(),
                new ExportManifestBuilder(),
                new ExportSummaryBuilder(),
                new ExportPathResolver());

            coordinator.Export(
                new GenerationResult
                {
                    World = new SyntheticEnterpriseWorld
                    {
                        CollaborationSites =
                        {
                            new CollaborationSite
                            {
                                Id = "SITE-001",
                                CompanyId = "CO-001",
                                Platform = "Teams",
                                Name = "Operations Workspace",
                                Url = "https://collab.contoso.test/sites/operations",
                                OwnerPersonId = "PER-001",
                                OwnerDepartmentId = "DEP-001",
                                MemberCount = "42",
                                FileCount = "900",
                                TotalSizeGb = "12",
                                PrivacyType = "Private",
                                WorkspaceType = "Department"
                            }
                        },
                        CollaborationChannels =
                        {
                            new CollaborationChannel
                            {
                                Id = "CHAN-001",
                                CompanyId = "CO-001",
                                CollaborationSiteId = "SITE-001",
                                Name = "General",
                                ChannelType = "Standard",
                                MemberCount = "42",
                                MessageCount = "5000",
                                FileCount = "300"
                            }
                        },
                        CollaborationChannelTabs =
                        {
                            new CollaborationChannelTab
                            {
                                Id = "TAB-001",
                                CompanyId = "CO-001",
                                CollaborationChannelId = "CHAN-001",
                                Name = "Files",
                                TabType = "DocumentLibrary",
                                TargetType = "DocumentLibrary",
                                TargetId = "LIB-001",
                                TargetReference = "Documents",
                                Vendor = "Microsoft",
                                IsPinned = true
                            }
                        },
                        DocumentLibraries =
                        {
                            new DocumentLibrary
                            {
                                Id = "LIB-001",
                                CompanyId = "CO-001",
                                CollaborationSiteId = "SITE-001",
                                Name = "Documents",
                                TemplateType = "Documents",
                                ItemCount = "1500",
                                TotalSizeGb = "18",
                                Sensitivity = "Internal"
                            }
                        },
                        SitePages =
                        {
                            new SitePage
                            {
                                Id = "PAGE-001",
                                CompanyId = "CO-001",
                                CollaborationSiteId = "SITE-001",
                                Title = "Operations Workspace Home",
                                PageType = "Home",
                                AuthorPersonId = "PER-001",
                                AssociatedLibraryId = "LIB-001",
                                ViewCount = "900",
                                LastModified = DateTimeOffset.Parse("2026-04-10T12:00:00Z"),
                                PromotedState = "None"
                            }
                        },
                        DocumentFolders =
                        {
                            new DocumentFolder
                            {
                                Id = "FOLDER-001",
                                CompanyId = "CO-001",
                                DocumentLibraryId = "LIB-001",
                                Name = "Shared",
                                Depth = "1",
                                ItemCount = "300",
                                TotalSizeGb = "4",
                                Sensitivity = "Internal"
                            },
                            new DocumentFolder
                            {
                                Id = "FOLDER-002",
                                CompanyId = "CO-001",
                                DocumentLibraryId = "LIB-001",
                                ParentFolderId = "FOLDER-001",
                                Name = "Shared-01",
                                Depth = "2",
                                ItemCount = "120",
                                TotalSizeGb = "2",
                                Sensitivity = "Internal"
                            }
                        }
                    },
                    Statistics = new GenerationStatistics()
                },
                new ExportRequest
                {
                    Format = ExportSerializationFormat.Json,
                    OutputPath = temp,
                    ArtifactPrefix = "topology",
                    IncludeManifest = true,
                    IncludeSummary = false
                });

            Assert.True(File.Exists(Path.Combine(outputRoot, "entities", "collaboration_channel_tabs.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "entities", "site_pages.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "entities", "document_folders.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "links", "collaboration_channel_tab_targets.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "links", "site_page_library_links.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "links", "document_folder_lineage.json")));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }
}
