using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;

namespace SyntheticEnterprise.Exporting.Services;

public sealed class ExportSummaryBuilder : IExportSummaryBuilder
{
    public ExportSummary Build(object generationResult, int artifactCount)
    {
        var result = Unwrap(generationResult);

        if (result is GenerationResult typedResult)
        {
            return new ExportSummary
            {
                CompanyCount = typedResult.Statistics.CompanyCount,
                OfficeCount = typedResult.Statistics.OfficeCount,
                PersonCount = typedResult.Statistics.PersonCount,
                AccountCount = typedResult.Statistics.AccountCount,
                GroupCount = typedResult.Statistics.GroupCount,
                ApplicationCount = typedResult.Statistics.ApplicationCount,
                DeviceCount = typedResult.Statistics.DeviceCount,
                RepositoryCount = typedResult.Statistics.RepositoryCount,
                ContainerCount = typedResult.Statistics.ContainerCount,
                OrganizationalUnitCount = typedResult.Statistics.OrganizationalUnitCount,
                PolicyCount = typedResult.Statistics.PolicyCount,
                PolicySettingCount = typedResult.Statistics.PolicySettingCount,
                PolicyTargetLinkCount = typedResult.Statistics.PolicyTargetLinkCount,
                PluginRecordCount = typedResult.World.PluginRecords.Count,
                AnomalyCount = typedResult.World.IdentityAnomalies.Count
                    + typedResult.World.InfrastructureAnomalies.Count
                    + typedResult.World.RepositoryAnomalies.Count,
                ArtifactCount = artifactCount
            };
        }

        if (result is SyntheticEnterpriseWorld world)
        {
            return new ExportSummary
            {
                CompanyCount = world.Companies.Count,
                OfficeCount = world.Offices.Count,
                PersonCount = world.People.Count,
                AccountCount = world.Accounts.Count,
                GroupCount = world.Groups.Count,
                ApplicationCount = world.Applications.Count,
                DeviceCount = world.Devices.Count + world.Servers.Count,
                RepositoryCount = world.Databases.Count
                    + world.FileShares.Count
                    + world.CollaborationSites.Count
                    + world.CollaborationChannels.Count
                    + world.CollaborationChannelTabs.Count
                    + world.DocumentLibraries.Count
                    + world.SitePages.Count
                    + world.DocumentFolders.Count,
                ContainerCount = world.Containers.Count,
                OrganizationalUnitCount = world.OrganizationalUnits.Count,
                PolicyCount = world.Policies.Count,
                PolicySettingCount = world.PolicySettings.Count,
                PolicyTargetLinkCount = world.PolicyTargetLinks.Count,
                PluginRecordCount = world.PluginRecords.Count,
                AnomalyCount = world.IdentityAnomalies.Count
                    + world.InfrastructureAnomalies.Count
                    + world.RepositoryAnomalies.Count,
                ArtifactCount = artifactCount
            };
        }

        return new ExportSummary
        {
            ArtifactCount = artifactCount
        };
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
