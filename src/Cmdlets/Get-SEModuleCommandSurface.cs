using System.Management.Automation;
using System.Reflection;
using SyntheticEnterprise.Module.Contracts;

namespace SyntheticEnterprise.Module.Cmdlets;

[Cmdlet(VerbsCommon.Get, "SEModuleCommandSurface")]
[OutputType(typeof(ModuleCommandDescriptor))]
public sealed class GetSEModuleCommandSurfaceCommand : PSCmdlet
{
    protected override void ProcessRecord()
    {
        var assembly = typeof(GetSEModuleCommandSurfaceCommand).Assembly;
        var commands = assembly.GetTypes()
            .Select(type => new
            {
                Type = type,
                Cmdlet = type.GetCustomAttribute<CmdletAttribute>()
            })
            .Where(x => x.Cmdlet is not null)
            .OrderBy(x => x.Cmdlet!.NounName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Cmdlet!.VerbName, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in commands)
        {
            var parameters = entry.Type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(property => property.GetCustomAttributes<ParameterAttribute>().Select(attribute => attribute.ParameterSetName))
                .Where(name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, ParameterAttribute.AllParameterSets, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .DefaultIfEmpty("Default")
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var acceptsPipeline = entry.Type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(property => property.GetCustomAttributes<ParameterAttribute>())
                .Any(attribute => attribute.ValueFromPipeline || attribute.ValueFromPipelineByPropertyName);

            WriteObject(new ModuleCommandDescriptor(
                $"{entry.Cmdlet!.VerbName}-{entry.Cmdlet.NounName}",
                Categorize(entry.Cmdlet),
                parameters,
                acceptsPipeline,
                entry.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Any(property => string.Equals(property.Name, "PassThru", StringComparison.OrdinalIgnoreCase))));
        }
    }

    private static string Categorize(CmdletAttribute cmdlet) => cmdlet.VerbName switch
    {
        "New" => "Generation",
        "Add" => "Enrichment",
        "Invoke" => "Mutation",
        "Export" => "Materialization",
        "Save" => "Persistence",
        "Import" => "Persistence",
        "Get" => "Discovery",
        "Resolve" => "Scenario",
        "Test" => "Scenario",
        "Merge" => "Scenario",
        _ => "General"
    };
}
