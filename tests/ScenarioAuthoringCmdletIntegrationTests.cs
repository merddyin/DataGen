using System.Management.Automation;
using SyntheticEnterprise.Contracts.Scenarios;
using SyntheticEnterprise.PowerShell.Cmdlets;

namespace SyntheticEnterprise.Tests;

public sealed class ScenarioAuthoringCmdletIntegrationTests
{
    [Fact]
    public void GetSEScenarioTemplate_Returns_Plugin_Authoring_Hints()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "tax.generator.json"), """
                {
                  "capability": "TaxIdentifiers",
                  "displayName": "Tax Identifiers",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "tax.plugin.ps1",
                  "parameters": [
                    {
                      "name": "Region",
                      "typeName": "System.String",
                      "required": true,
                      "defaultValue": "US"
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "tax.plugin.ps1"), "param()");

            using var powershell = System.Management.Automation.PowerShell.Create();
            powershell.AddCommand("Import-Module")
                .AddParameter("Name", typeof(NewSEEnterpriseWorldCommand).Assembly.Location);
            powershell.Invoke();
            Assert.False(powershell.HadErrors);

            powershell.Commands.Clear();
            powershell.AddCommand("Get-SEScenarioTemplate")
                .AddParameter("PluginRootPath", tempRoot)
                .AddParameter("EnablePluginCapability", "TaxIdentifiers");

            var results = powershell.Invoke<ScenarioTemplateDescriptor>();

            Assert.False(powershell.HadErrors);
            var template = Assert.Single(results, item => item.Kind == ScenarioTemplateKind.RegionalManufacturer);
            var hint = Assert.Single(template.PluginAuthoringHints, item => item.Capability == "TaxIdentifiers");
            Assert.Equal("US", hint.SuggestedSettings["Region"]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void NewSEScenarioFromTemplate_Hydrates_Plugin_Defaults_Into_Envelope()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "tax.generator.json"), """
                {
                  "capability": "TaxIdentifiers",
                  "displayName": "Tax Identifiers",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "tax.plugin.ps1",
                  "parameters": [
                    {
                      "name": "Region",
                      "typeName": "System.String",
                      "required": true,
                      "defaultValue": "US"
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "tax.plugin.ps1"), "param()");

            using var powershell = System.Management.Automation.PowerShell.Create();
            powershell.AddCommand("Import-Module")
                .AddParameter("Name", typeof(NewSEEnterpriseWorldCommand).Assembly.Location);
            powershell.Invoke();
            Assert.False(powershell.HadErrors);

            powershell.Commands.Clear();
            powershell.AddCommand("New-SEScenarioFromTemplate")
                .AddParameter("Template", ScenarioTemplateKind.RegionalManufacturer)
                .AddParameter("PluginRootPath", tempRoot)
                .AddParameter("EnablePluginCapability", "TaxIdentifiers");

            var result = Assert.Single(powershell.Invoke<ScenarioEnvelope>());

            Assert.False(powershell.HadErrors);
            var configuration = Assert.Single(result.ExternalPlugins!.CapabilityConfigurations);
            Assert.Equal("TaxIdentifiers", configuration.Capability);
            Assert.Equal("US", configuration.Settings["Region"]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"datagen-scenario-cmdlet-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
