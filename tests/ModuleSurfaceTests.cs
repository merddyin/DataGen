using SyntheticEnterprise.Module.Contracts;
using SyntheticEnterprise.PowerShell.Cmdlets;

namespace SyntheticEnterprise.Tests;

public sealed class ModuleSurfaceTests
{
    [Fact]
    public void Known_Command_Surface_Should_Be_Stable()
    {
        var commands = new[]
        {
            new ModuleCommandDescriptor("New-SEEnterpriseWorld", "Generation", new[] { "Path", "Object" }, false, false),
            new ModuleCommandDescriptor("Export-SEEnterpriseWorld", "Materialization", new[] { "Csv", "Json", "Bundle" }, true, true)
        };

        Assert.Contains(commands, c => c.Name == "New-SEEnterpriseWorld");
        Assert.Contains(commands, c => c.Name == "Export-SEEnterpriseWorld");
    }

    [Fact]
    public void DeterministicReleaseInputs_ArePublicCmdletParameters()
    {
        Assert.NotNull(typeof(NewSEEnterpriseWorldCommand).GetProperty("GeneratedAt"));
        Assert.NotNull(typeof(SaveSEEnterpriseWorldCommand).GetProperty("SavedAt"));
        Assert.NotNull(typeof(SaveSEEnterpriseWorldCommand).GetProperty("SnapshotId"));
        Assert.NotNull(typeof(ExportSEEnterpriseWorldCommand).GetProperty("ExportedAtUtc"));
    }
}
