namespace SyntheticEnterprise.PowerShell.Cmdlets;

using System.IO;
using System.Management.Automation;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Scenarios;

[Cmdlet(VerbsCommon.Get, "SEScenarioTemplate")]
[OutputType(typeof(ScenarioTemplateDescriptor))]
public sealed class GetSEScenarioTemplateCommand : PSCmdlet
{
    [Parameter(Mandatory = false)]
    public string[]? PluginRootPath { get; set; }

    [Parameter(Mandatory = false)]
    public string[]? EnablePluginCapability { get; set; }

    protected override void ProcessRecord()
    {
        using var services = ScenarioCmdletInput.BuildServices();
        var registry = services.GetRequiredService<IScenarioTemplateRegistry>();
        var validator = services.GetRequiredService<IScenarioValidator>();

        foreach (var descriptor in registry.GetTemplates())
        {
            if (PluginRootPath is null || PluginRootPath.Length == 0 || EnablePluginCapability is null || EnablePluginCapability.Length == 0)
            {
                WriteObject(descriptor);
                continue;
            }

            var template = ScenarioCmdletInput.ApplyExternalPlugins(registry.CreateTemplate(descriptor.Kind), PluginRootPath, EnablePluginCapability);
            var validation = validator.Validate(template);

            WriteObject(new ScenarioTemplateDescriptor
            {
                Kind = descriptor.Kind,
                Name = descriptor.Name,
                Description = descriptor.Description,
                RecommendedOverlays = descriptor.RecommendedOverlays.ToList(),
                PluginContributions = validation.Contributions,
                PluginAuthoringHints = validation.AuthoringHints
            });
        }
    }
}

[Cmdlet(VerbsCommon.New, "SEScenarioFromTemplate")]
[OutputType(typeof(ScenarioEnvelope))]
public sealed class NewSEScenarioFromTemplateCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)]
    public ScenarioTemplateKind Template { get; set; }

    [Parameter(Mandatory = false)]
    public string[]? PluginRootPath { get; set; }

    [Parameter(Mandatory = false)]
    public string[]? EnablePluginCapability { get; set; }

    protected override void ProcessRecord()
    {
        using var services = ScenarioCmdletInput.BuildServices();
        var registry = services.GetRequiredService<IScenarioTemplateRegistry>();
        var resolver = services.GetRequiredService<IScenarioDefaultsResolver>();
        var hydrator = services.GetRequiredService<IScenarioPluginProfileHydrator>();

        var template = registry.CreateTemplate(Template);
        if (PluginRootPath is not null && PluginRootPath.Length > 0 && EnablePluginCapability is not null && EnablePluginCapability.Length > 0)
        {
            template = ScenarioCmdletInput.ApplyExternalPlugins(template, PluginRootPath, EnablePluginCapability);
            var hydrated = hydrator.Hydrate(resolver.Resolve(template)).Scenario;
            template = ScenarioCmdletInput.ToEnvelope(hydrated);
        }

        WriteObject(template);
    }
}

[Cmdlet(VerbsData.Merge, "SEScenarioOverlay")]
[OutputType(typeof(ScenarioMergeResult))]
public sealed class MergeSEScenarioOverlayCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public object Scenario { get; set; } = default!;

    [Parameter(Mandatory = true)]
    public ScenarioOverlayKind[] Overlay { get; set; } = Array.Empty<ScenarioOverlayKind>();

    protected override void ProcessRecord()
    {
        using var services = ScenarioCmdletInput.BuildServices();
        var service = services.GetRequiredService<IScenarioOverlayService>();
        WriteObject(service.ApplyOverlays(ScenarioCmdletInput.Unwrap(Scenario), Overlay));
    }
}

[Cmdlet("Resolve", "SEScenario")]
[OutputType(typeof(ScenarioDefinition))]
public sealed class ResolveSEScenarioCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ParameterSetName = "Object", ValueFromPipeline = true)]
    public object Scenario { get; set; } = default!;

    [Parameter(Mandatory = true, ParameterSetName = "Path")]
    public string Path { get; set; } = string.Empty;

    [Parameter(Mandatory = true, ParameterSetName = "Json")]
    public string Json { get; set; } = string.Empty;

    protected override void ProcessRecord()
    {
        using var services = ScenarioCmdletInput.BuildServices();
        var resolver = services.GetRequiredService<IScenarioDefaultsResolver>();
        var hydrator = services.GetRequiredService<IScenarioPluginProfileHydrator>();
        var input = ParameterSetName switch
        {
            "Path" => File.ReadAllText(System.IO.Path.GetFullPath(Path)),
            "Json" => Json,
            _ => ScenarioCmdletInput.Unwrap(Scenario)
        };

        WriteObject(hydrator.Hydrate(resolver.Resolve(input)).Scenario);
    }
}

[Cmdlet(VerbsDiagnostic.Test, "SEScenario")]
[OutputType(typeof(ScenarioValidationResult))]
public sealed class TestSEScenarioCommand : PSCmdlet
{
    [Parameter(Mandatory = true, ParameterSetName = "Object", ValueFromPipeline = true)]
    public object Scenario { get; set; } = default!;

    [Parameter(Mandatory = true, ParameterSetName = "Path")]
    public string Path { get; set; } = string.Empty;

    [Parameter(Mandatory = true, ParameterSetName = "Json")]
    public string Json { get; set; } = string.Empty;

    protected override void ProcessRecord()
    {
        using var services = ScenarioCmdletInput.BuildServices();
        var validator = services.GetRequiredService<IScenarioValidator>();
        var input = ParameterSetName switch
        {
            "Path" => File.ReadAllText(System.IO.Path.GetFullPath(Path)),
            "Json" => Json,
            _ => ScenarioCmdletInput.Unwrap(Scenario)
        };

        WriteObject(validator.Validate(input));
    }
}

file static class ScenarioCmdletInput
{
    public static ServiceProvider BuildServices()
        => new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

    public static object Unwrap(object input)
        => input is PSObject psObject ? psObject.BaseObject : input;

    public static ScenarioEnvelope ApplyExternalPlugins(
        ScenarioEnvelope envelope,
        IReadOnlyCollection<string> pluginRootPaths,
        IReadOnlyCollection<string> enabledCapabilities)
    {
        var existingProfile = envelope.ExternalPlugins ?? new ExternalPluginScenarioProfile();
        return new ScenarioEnvelope
        {
            Name = envelope.Name,
            Description = envelope.Description,
            Template = envelope.Template,
            Overlays = envelope.Overlays.ToList(),
            CompanyCount = envelope.CompanyCount,
            IndustryProfile = envelope.IndustryProfile,
            GeographyProfile = envelope.GeographyProfile,
            DeviationProfile = envelope.DeviationProfile,
            EmployeeSize = envelope.EmployeeSize,
            Identity = envelope.Identity,
            Applications = envelope.Applications,
            Infrastructure = envelope.Infrastructure,
            Repositories = envelope.Repositories,
            Cmdb = envelope.Cmdb,
            ObservedData = envelope.ObservedData,
            ExternalPlugins = new ExternalPluginScenarioProfile
            {
                PluginRootPaths = existingProfile.PluginRootPaths.Concat(pluginRootPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                EnabledCapabilities = existingProfile.EnabledCapabilities.Concat(enabledCapabilities).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                CapabilityConfigurations = existingProfile.CapabilityConfigurations.ToList(),
                AllowAssemblyPlugins = existingProfile.AllowAssemblyPlugins,
                ExecutionTimeoutSeconds = existingProfile.ExecutionTimeoutSeconds,
                MaxGeneratedRecords = existingProfile.MaxGeneratedRecords,
                MaxWarningCount = existingProfile.MaxWarningCount,
                MaxDiagnosticEntries = existingProfile.MaxDiagnosticEntries,
                MaxDiagnosticCharacters = existingProfile.MaxDiagnosticCharacters,
                MaxInputPayloadBytes = existingProfile.MaxInputPayloadBytes,
                MaxOutputPayloadBytes = existingProfile.MaxOutputPayloadBytes,
                RequireContentHashAllowList = existingProfile.RequireContentHashAllowList,
                RequireAssemblyHashApproval = existingProfile.RequireAssemblyHashApproval,
                AllowedContentHashes = existingProfile.AllowedContentHashes.ToList()
            },
            Anomalies = envelope.Anomalies.ToList(),
            Companies = envelope.Companies.ToList(),
            OfficeCount = envelope.OfficeCount
        };
    }

    public static ScenarioEnvelope ToEnvelope(ScenarioDefinition definition)
        => new()
        {
            Name = definition.Name,
            Description = definition.Description,
            CompanyCount = definition.CompanyCount,
            IndustryProfile = definition.IndustryProfile,
            GeographyProfile = definition.GeographyProfile,
            DeviationProfile = definition.DeviationProfile,
            EmployeeSize = definition.EmployeeSize,
            Identity = definition.Identity,
            Applications = definition.Applications,
            Infrastructure = definition.Infrastructure,
            Repositories = definition.Repositories,
            Cmdb = definition.Cmdb,
            ObservedData = definition.ObservedData,
            ExternalPlugins = definition.ExternalPlugins,
            Anomalies = definition.Anomalies.ToList(),
            Companies = definition.Companies.ToList(),
            OfficeCount = definition.Companies.Count > 0 ? definition.Companies.Max(company => company.OfficeCount) : null
        };
}
