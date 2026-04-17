using System.Collections.Generic;
using System.Linq;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Services;

namespace SyntheticEnterprise.Exporting.Profiles;

public sealed class NormalizedLinkTableProvider : ILinkTableProvider, IExportRequestAware
{
    public void ApplyRequest(ExportRequest request)
    {
        _ = request;
    }

    public IReadOnlyList<object> GetDescriptors()
    {
        return new object[]
        {
            new LinkTableDescriptor<DirectoryGroupMembership>
            {
                LogicalName = "group_memberships",
                RelativePathStem = "links/group_memberships",
                Columns =
                [
                    "id",
                    "group_id",
                    "member_object_id",
                    "member_object_type"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.GroupMemberships,
                RowProjector = membership => new Dictionary<string, object?>
                {
                    ["id"] = membership.Id,
                    ["group_id"] = membership.GroupId,
                    ["member_object_id"] = membership.MemberObjectId,
                    ["member_object_type"] = membership.MemberObjectType
                },
                SortKeySelector = membership => membership.Id
            },
            new LinkTableDescriptor<PolicyTargetLink>
            {
                LogicalName = "policy_target_links",
                RelativePathStem = "links/policy_target_links",
                Columns =
                [
                    "id",
                    "company_id",
                    "policy_id",
                    "target_type",
                    "target_id",
                    "assignment_mode",
                    "link_enabled",
                    "is_enforced",
                    "link_order",
                    "filter_type",
                    "filter_value"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.PolicyTargetLinks,
                RowProjector = link => new Dictionary<string, object?>
                {
                    ["id"] = link.Id,
                    ["company_id"] = link.CompanyId,
                    ["policy_id"] = link.PolicyId,
                    ["target_type"] = link.TargetType,
                    ["target_id"] = link.TargetId,
                    ["assignment_mode"] = link.AssignmentMode,
                    ["link_enabled"] = link.LinkEnabled,
                    ["is_enforced"] = link.IsEnforced,
                    ["link_order"] = link.LinkOrder,
                    ["filter_type"] = link.FilterType,
                    ["filter_value"] = link.FilterValue
                },
                SortKeySelector = link => link.Id
            },
            new LinkTableDescriptor<ConfigurationItemRelationship>
            {
                LogicalName = "configuration_item_relationships",
                RelativePathStem = "links/configuration_item_relationships",
                Columns =
                [
                    "id",
                    "company_id",
                    "source_configuration_item_id",
                    "target_configuration_item_id",
                    "relationship_type",
                    "is_primary",
                    "confidence",
                    "source_evidence",
                    "notes"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ConfigurationItemRelationships,
                RowProjector = relationship => new Dictionary<string, object?>
                {
                    ["id"] = relationship.Id,
                    ["company_id"] = relationship.CompanyId,
                    ["source_configuration_item_id"] = relationship.SourceConfigurationItemId,
                    ["target_configuration_item_id"] = relationship.TargetConfigurationItemId,
                    ["relationship_type"] = relationship.RelationshipType,
                    ["is_primary"] = relationship.IsPrimary,
                    ["confidence"] = relationship.Confidence,
                    ["source_evidence"] = relationship.SourceEvidence,
                    ["notes"] = relationship.Notes
                },
                SortKeySelector = relationship => relationship.Id
            },
            new LinkTableDescriptor<CmdbSourceLink>
            {
                LogicalName = "cmdb_source_links",
                RelativePathStem = "links/cmdb_source_links",
                Columns =
                [
                    "id",
                    "company_id",
                    "source_record_id",
                    "configuration_item_id",
                    "link_type",
                    "match_method",
                    "confidence"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.CmdbSourceLinks,
                RowProjector = link => new Dictionary<string, object?>
                {
                    ["id"] = link.Id,
                    ["company_id"] = link.CompanyId,
                    ["source_record_id"] = link.SourceRecordId,
                    ["configuration_item_id"] = link.ConfigurationItemId,
                    ["link_type"] = link.LinkType,
                    ["match_method"] = link.MatchMethod,
                    ["confidence"] = link.Confidence
                },
                SortKeySelector = link => link.Id
            },
            new LinkTableDescriptor<CmdbSourceRelationship>
            {
                LogicalName = "cmdb_source_relationships",
                RelativePathStem = "links/cmdb_source_relationships",
                Columns =
                [
                    "id",
                    "company_id",
                    "source_system",
                    "source_relationship_id",
                    "source_record_id",
                    "target_record_id",
                    "relationship_type",
                    "is_primary",
                    "confidence",
                    "status"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.CmdbSourceRelationships,
                RowProjector = relationship => new Dictionary<string, object?>
                {
                    ["id"] = relationship.Id,
                    ["company_id"] = relationship.CompanyId,
                    ["source_system"] = relationship.SourceSystem,
                    ["source_relationship_id"] = relationship.SourceRelationshipId,
                    ["source_record_id"] = relationship.SourceRecordId,
                    ["target_record_id"] = relationship.TargetRecordId,
                    ["relationship_type"] = relationship.RelationshipType,
                    ["is_primary"] = relationship.IsPrimary,
                    ["confidence"] = relationship.Confidence,
                    ["status"] = relationship.Status
                },
                SortKeySelector = relationship => relationship.Id
            },
            new LinkTableDescriptor<ApplicationDependency>
            {
                LogicalName = "application_dependencies",
                RelativePathStem = "links/application_dependencies",
                Columns =
                [
                    "id",
                    "company_id",
                    "source_application_id",
                    "target_application_id",
                    "dependency_type",
                    "interface_type",
                    "criticality"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ApplicationDependencies,
                RowProjector = dependency => new Dictionary<string, object?>
                {
                    ["id"] = dependency.Id,
                    ["company_id"] = dependency.CompanyId,
                    ["source_application_id"] = dependency.SourceApplicationId,
                    ["target_application_id"] = dependency.TargetApplicationId,
                    ["dependency_type"] = dependency.DependencyType,
                    ["interface_type"] = dependency.InterfaceType,
                    ["criticality"] = dependency.Criticality
                },
                SortKeySelector = dependency => dependency.Id
            },
            new LinkTableDescriptor<ApplicationRepositoryLink>
            {
                LogicalName = "application_repository_links",
                RelativePathStem = "links/application_repository_links",
                Columns =
                [
                    "id",
                    "company_id",
                    "application_id",
                    "repository_id",
                    "repository_type",
                    "relationship_type",
                    "criticality"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ApplicationRepositoryLinks,
                RowProjector = link => new Dictionary<string, object?>
                {
                    ["id"] = link.Id,
                    ["company_id"] = link.CompanyId,
                    ["application_id"] = link.ApplicationId,
                    ["repository_id"] = link.RepositoryId,
                    ["repository_type"] = link.RepositoryType,
                    ["relationship_type"] = link.RelationshipType,
                    ["criticality"] = link.Criticality
                },
                SortKeySelector = link => link.Id
            },
            new LinkTableDescriptor<ApplicationServiceDependency>
            {
                LogicalName = "application_service_dependencies",
                RelativePathStem = "links/application_service_dependencies",
                Columns =
                [
                    "id",
                    "company_id",
                    "source_service_id",
                    "target_service_id",
                    "dependency_type",
                    "interface_type",
                    "criticality"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ApplicationServiceDependencies,
                RowProjector = dependency => new Dictionary<string, object?>
                {
                    ["id"] = dependency.Id,
                    ["company_id"] = dependency.CompanyId,
                    ["source_service_id"] = dependency.SourceServiceId,
                    ["target_service_id"] = dependency.TargetServiceId,
                    ["dependency_type"] = dependency.DependencyType,
                    ["interface_type"] = dependency.InterfaceType,
                    ["criticality"] = dependency.Criticality
                },
                SortKeySelector = dependency => dependency.Id
            },
            new LinkTableDescriptor<ApplicationServiceHosting>
            {
                LogicalName = "application_service_hostings",
                RelativePathStem = "links/application_service_hostings",
                Columns =
                [
                    "id",
                    "company_id",
                    "application_service_id",
                    "host_type",
                    "host_id",
                    "host_name",
                    "hosting_role",
                    "deployment_model"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ApplicationServiceHostings,
                RowProjector = hosting => new Dictionary<string, object?>
                {
                    ["id"] = hosting.Id,
                    ["company_id"] = hosting.CompanyId,
                    ["application_service_id"] = hosting.ApplicationServiceId,
                    ["host_type"] = hosting.HostType,
                    ["host_id"] = hosting.HostId,
                    ["host_name"] = hosting.HostName,
                    ["hosting_role"] = hosting.HostingRole,
                    ["deployment_model"] = hosting.DeploymentModel
                },
                SortKeySelector = hosting => hosting.Id
            },
            new LinkTableDescriptor<DeviceSoftwareInstallation>
            {
                LogicalName = "device_software_installations",
                RelativePathStem = "links/device_software_installations",
                Columns =
                [
                    "id",
                    "device_id",
                    "software_id"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.DeviceSoftwareInstallations,
                RowProjector = installation => new Dictionary<string, object?>
                {
                    ["id"] = installation.Id,
                    ["device_id"] = installation.DeviceId,
                    ["software_id"] = installation.SoftwareId
                },
                SortKeySelector = installation => installation.Id
            },
            new LinkTableDescriptor<ServerSoftwareInstallation>
            {
                LogicalName = "server_software_installations",
                RelativePathStem = "links/server_software_installations",
                Columns =
                [
                    "id",
                    "server_id",
                    "software_id"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ServerSoftwareInstallations,
                RowProjector = installation => new Dictionary<string, object?>
                {
                    ["id"] = installation.Id,
                    ["server_id"] = installation.ServerId,
                    ["software_id"] = installation.SoftwareId
                },
                SortKeySelector = installation => installation.Id
            },
            new LinkTableDescriptor<ApplicationTenantLink>
            {
                LogicalName = "application_tenant_links",
                RelativePathStem = "links/application_tenant_links",
                Columns =
                [
                    "id",
                    "company_id",
                    "application_id",
                    "cloud_tenant_id",
                    "relationship_type",
                    "is_primary"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ApplicationTenantLinks,
                RowProjector = link => new Dictionary<string, object?>
                {
                    ["id"] = link.Id,
                    ["company_id"] = link.CompanyId,
                    ["application_id"] = link.ApplicationId,
                    ["cloud_tenant_id"] = link.CloudTenantId,
                    ["relationship_type"] = link.RelationshipType,
                    ["is_primary"] = link.IsPrimary
                },
                SortKeySelector = link => link.Id
            },
            new LinkTableDescriptor<ApplicationBusinessProcessLink>
            {
                LogicalName = "application_business_process_links",
                RelativePathStem = "links/application_business_process_links",
                Columns =
                [
                    "id",
                    "company_id",
                    "application_id",
                    "business_process_id",
                    "relationship_type",
                    "is_primary"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ApplicationBusinessProcessLinks,
                RowProjector = link => new Dictionary<string, object?>
                {
                    ["id"] = link.Id,
                    ["company_id"] = link.CompanyId,
                    ["application_id"] = link.ApplicationId,
                    ["business_process_id"] = link.BusinessProcessId,
                    ["relationship_type"] = link.RelationshipType,
                    ["is_primary"] = link.IsPrimary
                },
                SortKeySelector = link => link.Id
            },
            new LinkTableDescriptor<ApplicationCounterpartyLink>
            {
                LogicalName = "application_counterparty_links",
                RelativePathStem = "links/application_counterparty_links",
                Columns =
                [
                    "id",
                    "company_id",
                    "application_id",
                    "external_organization_id",
                    "relationship_type",
                    "integration_type",
                    "criticality"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.ApplicationCounterpartyLinks,
                RowProjector = link => new Dictionary<string, object?>
                {
                    ["id"] = link.Id,
                    ["company_id"] = link.CompanyId,
                    ["application_id"] = link.ApplicationId,
                    ["external_organization_id"] = link.ExternalOrganizationId,
                    ["relationship_type"] = link.RelationshipType,
                    ["integration_type"] = link.IntegrationType,
                    ["criticality"] = link.Criticality
                },
                SortKeySelector = link => link.Id
            },
            new LinkTableDescriptor<BusinessProcessCounterpartyLink>
            {
                LogicalName = "business_process_counterparty_links",
                RelativePathStem = "links/business_process_counterparty_links",
                Columns =
                [
                    "id",
                    "company_id",
                    "business_process_id",
                    "external_organization_id",
                    "relationship_type",
                    "is_primary"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.BusinessProcessCounterpartyLinks,
                RowProjector = link => new Dictionary<string, object?>
                {
                    ["id"] = link.Id,
                    ["company_id"] = link.CompanyId,
                    ["business_process_id"] = link.BusinessProcessId,
                    ["external_organization_id"] = link.ExternalOrganizationId,
                    ["relationship_type"] = link.RelationshipType,
                    ["is_primary"] = link.IsPrimary
                },
                SortKeySelector = link => link.Id
            },
            new LinkTableDescriptor<RepositoryAccessGrant>
            {
                LogicalName = "repository_access_grants",
                RelativePathStem = "links/repository_access_grants",
                Columns =
                [
                    "id",
                    "repository_id",
                    "repository_type",
                    "principal_object_id",
                    "principal_type",
                    "access_level"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.RepositoryAccessGrants,
                RowProjector = grant => new Dictionary<string, object?>
                {
                    ["id"] = grant.Id,
                    ["repository_id"] = grant.RepositoryId,
                    ["repository_type"] = grant.RepositoryType,
                    ["principal_object_id"] = grant.PrincipalObjectId,
                    ["principal_type"] = grant.PrincipalType,
                    ["access_level"] = grant.AccessLevel
                },
                SortKeySelector = grant => grant.Id
            },
            new LinkTableDescriptor<CollaborationChannelTab>
            {
                LogicalName = "collaboration_channel_tab_targets",
                RelativePathStem = "links/collaboration_channel_tab_targets",
                Columns =
                [
                    "id",
                    "company_id",
                    "collaboration_channel_id",
                    "target_type",
                    "target_id",
                    "target_reference"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.CollaborationChannelTabs
                    .Where(tab => !string.IsNullOrWhiteSpace(tab.TargetId) || !string.IsNullOrWhiteSpace(tab.TargetReference))
                    .ToList(),
                RowProjector = tab => new Dictionary<string, object?>
                {
                    ["id"] = tab.Id,
                    ["company_id"] = tab.CompanyId,
                    ["collaboration_channel_id"] = tab.CollaborationChannelId,
                    ["target_type"] = tab.TargetType,
                    ["target_id"] = tab.TargetId,
                    ["target_reference"] = tab.TargetReference
                },
                SortKeySelector = tab => tab.Id
            },
            new LinkTableDescriptor<SitePage>
            {
                LogicalName = "site_page_library_links",
                RelativePathStem = "links/site_page_library_links",
                Columns =
                [
                    "id",
                    "company_id",
                    "collaboration_site_id",
                    "associated_library_id"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.SitePages
                    .Where(page => !string.IsNullOrWhiteSpace(page.AssociatedLibraryId))
                    .ToList(),
                RowProjector = page => new Dictionary<string, object?>
                {
                    ["id"] = page.Id,
                    ["company_id"] = page.CompanyId,
                    ["collaboration_site_id"] = page.CollaborationSiteId,
                    ["associated_library_id"] = page.AssociatedLibraryId
                },
                SortKeySelector = page => page.Id
            },
            new LinkTableDescriptor<DocumentFolder>
            {
                LogicalName = "document_folder_lineage",
                RelativePathStem = "links/document_folder_lineage",
                Columns =
                [
                    "id",
                    "company_id",
                    "document_library_id",
                    "parent_folder_id",
                    "child_folder_id"
                ],
                RecordAccessor = result => GetGenerationResult(result).World.DocumentFolders
                    .Where(folder => !string.IsNullOrWhiteSpace(folder.ParentFolderId))
                    .ToList(),
                RowProjector = folder => new Dictionary<string, object?>
                {
                    ["id"] = folder.Id,
                    ["company_id"] = folder.CompanyId,
                    ["document_library_id"] = folder.DocumentLibraryId,
                    ["parent_folder_id"] = folder.ParentFolderId,
                    ["child_folder_id"] = folder.Id
                },
                SortKeySelector = folder => folder.Id
            }
        };
    }

    private static GenerationResult GetGenerationResult(object input)
    {
        var candidate = Unwrap(input);
        return candidate as GenerationResult
               ?? throw new InvalidOperationException("Normalized link export expects a GenerationResult input.");
    }

    private static object? Unwrap(object? input)
    {
        if (input is null)
        {
            return null;
        }

        if (input.GetType().FullName == "System.Management.Automation.PSObject")
        {
            return input.GetType().GetProperty("BaseObject")?.GetValue(input);
        }

        return input;
    }
}
