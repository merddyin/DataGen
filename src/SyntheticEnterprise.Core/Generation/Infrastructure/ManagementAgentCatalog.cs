namespace SyntheticEnterprise.Core.Generation.Infrastructure;

using SyntheticEnterprise.Contracts.Models;

/// <summary>
/// Names DataGen treats as endpoint deployment or configuration-management
/// agents. These packages are installed only when generated management evidence
/// establishes the corresponding path.
/// </summary>
internal static class ManagementAgentCatalog
{
    private static readonly HashSet<string> KnownDeploymentAgentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft Intune Management Extension",
        "Configuration Manager Client",
        "SCCM Client",
        "Tanium Client",
        "BigFix Client",
        "Remote Monitoring Agent",
        "Ansible Automation Platform",
        "Jamf Pro Management Agent",
        "Puppet Agent",
        "ServiceNow Agent",
    };

    internal static bool IsKnownDeploymentAgent(SoftwarePackage software)
        => IsKnownDeploymentAgentName(software.Name);

    internal static bool IsKnownDeploymentAgentName(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && KnownDeploymentAgentNames.Contains(name);
}
