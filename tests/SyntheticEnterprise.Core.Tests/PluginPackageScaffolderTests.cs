using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Plugins;

namespace SyntheticEnterprise.Core.Tests;

public sealed class PluginPackageScaffolderTests
{
    [Fact]
    public void Scaffolder_Creates_A_Valid_PowerShell_Pack_Package()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"datagen-plugin-scaffold-{Guid.NewGuid():N}");

        try
        {
            var scaffolder = new GenerationPluginPackageScaffolder();
            var result = scaffolder.Scaffold(new GenerationPluginPackageScaffoldRequest
            {
                RootPath = tempRoot,
                Capability = "Contoso.RiskOps",
                DisplayName = "Contoso RiskOps"
            });

            Assert.Equal(Path.GetFullPath(tempRoot), result.RootPath);
            Assert.Equal("Contoso.RiskOps", result.Capability);
            Assert.Equal("Contoso RiskOps", result.DisplayName);
            Assert.True(File.Exists(result.ManifestPath));
            Assert.True(File.Exists(result.EntryPointPath));
            Assert.True(File.Exists(result.ReadmePath));

            var securityPolicy = new DataOnlyGenerationPluginSecurityPolicy();
            var catalog = new FileSystemExternalGenerationPluginCatalog(
                new GenerationPluginManifestValidator(securityPolicy),
                securityPolicy,
                new AllowListExternalPluginTrustPolicy());
            var validator = new GenerationPluginPackageValidator(catalog);
            var report = Assert.Single(validator.Validate(
                new[] { tempRoot },
                new ExternalPluginExecutionSettings
                {
                    Enabled = true,
                    PluginRootPaths = new() { tempRoot },
                    RequireAssemblyHashApproval = false
                }));

            Assert.False(report.HasErrors);
            Assert.Equal(1, report.PluginCount);
            Assert.Equal(1, report.ValidCount);
            Assert.Equal(1, report.EligibleCount);
            var plugin = Assert.Single(report.Plugins);
            Assert.Equal("Contoso.RiskOps", plugin.Capability);
            Assert.Equal(PluginExecutionMode.PowerShellScript, plugin.ExecutionMode);
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
    public void Validator_Flags_Invalid_Pack_Contract_Metadata_When_Requested()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"datagen-plugin-pack-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "riskops.generator.json"), """
                {
                  "capability": "Contoso.RiskOps",
                  "displayName": "Contoso RiskOps",
                  "pluginKind": "SdkExample",
                  "executionMode": "PowerShellScript",
                  "entryPoint": "riskops.pack.ps1",
                  "security": {
                    "dataOnly": true,
                    "requestedCapabilities": [ "EmitDiagnostics" ]
                  },
                  "metadata": {
                    "packId": "Contoso.Mismatch",
                    "packPhase": "Preflight"
                  }
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "riskops.pack.ps1"), "New-PluginResult -Records @() -Warnings @()");

            var securityPolicy = new DataOnlyGenerationPluginSecurityPolicy();
            var catalog = new FileSystemExternalGenerationPluginCatalog(
                new GenerationPluginManifestValidator(securityPolicy),
                securityPolicy,
                new AllowListExternalPluginTrustPolicy());
            var validator = new GenerationPluginPackageValidator(catalog);
            var report = Assert.Single(validator.Validate(
                new[] { tempRoot },
                new ExternalPluginExecutionSettings
                {
                    Enabled = true,
                    PluginRootPaths = new() { tempRoot },
                    RequireAssemblyHashApproval = false
                },
                validatePackContract: true));

            Assert.True(report.PackContractChecked);
            Assert.True(report.HasErrors);
            Assert.True(report.PackContractErrorCount >= 3);
            Assert.Contains(report.PackContractIssues, issue => issue.RuleId == "pack-kind");
            Assert.Contains(report.PackContractIssues, issue => issue.RuleId == "pack-id-mismatch");
            Assert.Contains(report.PackContractIssues, issue => issue.RuleId == "pack-generate-data-missing");
            Assert.Contains(report.PackContractIssues, issue => issue.RuleId == "pack-phase-nonstandard" && !issue.IsError);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
