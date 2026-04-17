using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Plugins;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

var requestPath = GetArgument(args, "--request");
var responsePath = GetArgument(args, "--response");

if (string.IsNullOrWhiteSpace(requestPath) || string.IsNullOrWhiteSpace(responsePath))
{
    return 2;
}

ExternalPluginExecutionResponse response;

try
{
    var request = JsonSerializer.Deserialize<ExternalPluginExecutionRequest>(File.ReadAllText(requestPath), options)
        ?? throw new InvalidOperationException("Plugin execution request could not be deserialized.");

    if (string.IsNullOrWhiteSpace(request.Manifest.EntryPoint))
    {
        throw new InvalidOperationException("Plugin manifest does not declare an assembly entry point.");
    }

    response = ExecutePlugin(request);
}
catch (Exception ex)
{
    response = new ExternalPluginExecutionResponse
    {
        Executed = false,
        Warnings = new()
        {
            $"Assembly host failed: {ex.Message}"
        }
    };
}

Directory.CreateDirectory(Path.GetDirectoryName(responsePath)!);
File.WriteAllText(responsePath, JsonSerializer.Serialize(response, options));
return response.Executed ? 0 : 1;

static string? GetArgument(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static ExternalPluginExecutionResponse ExecutePlugin(ExternalPluginExecutionRequest request)
{
    var loadContext = new PluginLoadContext(request.Manifest.EntryPoint!);

    try
    {
        var assembly = loadContext.LoadFromAssemblyPath(request.Manifest.EntryPoint!);
        var pluginType = assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IExternalGenerationAssemblyPlugin).IsAssignableFrom(type))
            .FirstOrDefault(type =>
            {
                if (Activator.CreateInstance(type) is not IExternalGenerationAssemblyPlugin plugin)
                {
                    return false;
                }

                return string.Equals(plugin.Capability, request.Manifest.Capability, StringComparison.OrdinalIgnoreCase);
            });

        if (pluginType is null)
        {
            return new ExternalPluginExecutionResponse
            {
                Executed = false,
                Warnings = new()
                {
                    $"No assembly plugin implementing capability '{request.Manifest.Capability}' was found in '{request.Manifest.EntryPoint}'."
                }
            };
        }

        if (Activator.CreateInstance(pluginType) is not IExternalGenerationAssemblyPlugin pluginInstance)
        {
            return new ExternalPluginExecutionResponse
            {
                Executed = false,
                Warnings = new()
                {
                    $"Plugin type '{pluginType.FullName}' could not be instantiated."
                }
            };
        }

        return pluginInstance.Execute(request) ?? new ExternalPluginExecutionResponse
        {
            Executed = false,
            Warnings = new()
            {
                $"Plugin '{request.Manifest.Capability}' returned no response."
            }
        };
    }
    finally
    {
        loadContext.Unload();
    }
}

sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginAssemblyPath)
        : base(nameof(PluginLoadContext), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, "SyntheticEnterprise.Contracts", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return resolvedPath is null ? null : LoadFromAssemblyPath(resolvedPath);
    }
}
