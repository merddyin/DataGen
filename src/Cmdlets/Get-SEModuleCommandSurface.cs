using System.Management.Automation;
using SyntheticEnterprise.Module.Contracts;

namespace SyntheticEnterprise.Module.Cmdlets;

[Cmdlet(VerbsCommon.Get, "SEModuleCommandSurface")]
[OutputType(typeof(ModuleCommandDescriptor))]
public sealed class GetSEModuleCommandSurfaceCommand : PSCmdlet
{
    protected override void ProcessRecord()
    {
        var commands = new[]
        {
            new ModuleCommandDescriptor("New-SEEnterpriseWorld", "Generation", new[] { "Path", "Object" }, false, false),
            new ModuleCommandDescriptor("Add-SEIdentityLayer", "Enrichment", new[] { "Default" }, true, true),
            new ModuleCommandDescriptor("Add-SEInfrastructureLayer", "Enrichment", new[] { "Default" }, true, true),
            new ModuleCommandDescriptor("Add-SERepositoryLayer", "Enrichment", new[] { "Default" }, true, true),
            new ModuleCommandDescriptor("Invoke-SEAnomalyProfile", "Mutation", new[] { "Default" }, true, true),
            new ModuleCommandDescriptor("Export-SEEnterpriseWorld", "Materialization", new[] { "Csv", "Json", "Bundle" }, true, true),
            new ModuleCommandDescriptor("Save-SEEnterpriseWorld", "Persistence", new[] { "Default" }, true, false),
            new ModuleCommandDescriptor("Import-SEEnterpriseWorld", "Persistence", new[] { "Default" }, false, false),
        };

        foreach (var command in commands)
        {
            WriteObject(command);
        }
    }
}
