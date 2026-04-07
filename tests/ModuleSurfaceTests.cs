using SyntheticEnterprise.Module.Contracts;

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
}
