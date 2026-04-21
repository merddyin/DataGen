namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Plugins;
using SyntheticEnterprise.Core.Scenarios;

[Cmdlet(VerbsCommon.New, "SEEnterpriseWorld")]
[OutputType(typeof(GenerationResult))]
public sealed class NewSEEnterpriseWorldCommand : PSCmdlet
{
    [Parameter(Mandatory = false)]
    public string? CatalogRootPath { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = "Path")]
    public string? ScenarioPath { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = "Json")]
    public string? ScenarioJson { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = "Object", ValueFromPipeline = true)]
    public object? Scenario { get; set; }

    [Parameter(Mandatory = false)]
    public int? Seed { get; set; }

    [Parameter(Mandatory = false)]
    public string[]? PluginRootPath { get; set; }

    [Parameter(Mandatory = false)]
    public string[]? EnablePluginCapability { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter UseRegisteredPlugins { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter AllowAssemblyPlugins { get; set; }

    [Parameter(Mandatory = false)]
    public int PluginExecutionTimeoutSeconds { get; set; } = 10;

    [Parameter(Mandatory = false)]
    public int PluginMaxGeneratedRecords { get; set; } = 5000;

    [Parameter(Mandatory = false)]
    public int PluginMaxWarnings { get; set; } = 100;

    [Parameter(Mandatory = false)]
    public int PluginMaxDiagnosticEntries { get; set; } = 32;

    [Parameter(Mandatory = false)]
    public int PluginMaxDiagnosticCharacters { get; set; } = 4096;

    [Parameter(Mandatory = false)]
    public int PluginMaxInputPayloadBytes { get; set; } = 64 * 1024 * 1024;

    [Parameter(Mandatory = false)]
    public int PluginMaxOutputPayloadBytes { get; set; } = 64 * 1024 * 1024;

    [Parameter(Mandatory = false)]
    public string[]? PluginAllowedContentHash { get; set; }

    [Parameter(Mandatory = false)]
    public SwitchParameter RequirePluginHashApproval { get; set; }

    protected override void ProcessRecord()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var catalogLoader = services.GetRequiredService<ICatalogLoader>();
        var scenarioLoader = services.GetRequiredService<IScenarioLoader>();
        var worldGenerator = services.GetRequiredService<IWorldGenerator>();
        var registrationService = services.GetRequiredService<IGenerationPluginRegistrationService>();
        var pluginBindingService = services.GetRequiredService<IExternalPluginScenarioBindingService>();
        var pluginProfileHydrator = services.GetRequiredService<IScenarioPluginProfileHydrator>();

        var catalogs = string.IsNullOrWhiteSpace(CatalogRootPath)
            ? catalogLoader.LoadDefault()
            : catalogLoader.LoadFromPath(CatalogRootPath!);

        var scenario = ParameterSetName switch
        {
            "Json" => scenarioLoader.LoadFromJson(ScenarioJson!),
            "Object" => services.GetRequiredService<IScenarioDefaultsResolver>().Resolve(Scenario is PSObject psObject ? psObject.BaseObject : Scenario!),
            _ => scenarioLoader.LoadFromPath(ScenarioPath!)
        };
        scenario = pluginProfileHydrator.Hydrate(scenario).Scenario;

        var pluginSettings = pluginBindingService.Bind(
            scenario.ExternalPlugins,
            new ExternalPluginExecutionOverrides
        {
            PluginRootPaths = MyInvocation.BoundParameters.ContainsKey(nameof(PluginRootPath))
                ? PluginRootPath?.ToList() ?? new()
                : null,
            EnabledCapabilities = MyInvocation.BoundParameters.ContainsKey(nameof(EnablePluginCapability))
                ? EnablePluginCapability?.ToList() ?? new()
                : null,
            AllowAssemblyPlugins = MyInvocation.BoundParameters.ContainsKey(nameof(AllowAssemblyPlugins))
                ? AllowAssemblyPlugins.IsPresent
                : null,
            ExecutionTimeoutSeconds = MyInvocation.BoundParameters.ContainsKey(nameof(PluginExecutionTimeoutSeconds))
                ? PluginExecutionTimeoutSeconds
                : null,
            MaxGeneratedRecords = MyInvocation.BoundParameters.ContainsKey(nameof(PluginMaxGeneratedRecords))
                ? PluginMaxGeneratedRecords
                : null,
            MaxWarningCount = MyInvocation.BoundParameters.ContainsKey(nameof(PluginMaxWarnings))
                ? PluginMaxWarnings
                : null,
            MaxDiagnosticEntries = MyInvocation.BoundParameters.ContainsKey(nameof(PluginMaxDiagnosticEntries))
                ? PluginMaxDiagnosticEntries
                : null,
            MaxDiagnosticCharacters = MyInvocation.BoundParameters.ContainsKey(nameof(PluginMaxDiagnosticCharacters))
                ? PluginMaxDiagnosticCharacters
                : null,
            MaxInputPayloadBytes = MyInvocation.BoundParameters.ContainsKey(nameof(PluginMaxInputPayloadBytes))
                ? PluginMaxInputPayloadBytes
                : null,
            MaxOutputPayloadBytes = MyInvocation.BoundParameters.ContainsKey(nameof(PluginMaxOutputPayloadBytes))
                ? PluginMaxOutputPayloadBytes
                : null,
            RequireContentHashAllowList = MyInvocation.BoundParameters.ContainsKey(nameof(RequirePluginHashApproval))
                ? RequirePluginHashApproval.IsPresent
                : null,
            AllowedContentHashes = MyInvocation.BoundParameters.ContainsKey(nameof(PluginAllowedContentHash))
                ? PluginAllowedContentHash?.ToList() ?? new()
                : null
        });

        if (UseRegisteredPlugins.IsPresent)
        {
            pluginSettings = registrationService.ApplyRegistrations(pluginSettings, includeAllRegisteredCapabilities: true);
        }

        var context = new GenerationContext
        {
            Scenario = scenario,
            Seed = Seed,
            Metadata = new Dictionary<string, string?>
            {
                ["CatalogRootPath"] = CatalogRootPath
            },
            ExternalPlugins = pluginSettings
        };

        var result = worldGenerator.Generate(context, catalogs);

        WriteObject(result);
    }
}
