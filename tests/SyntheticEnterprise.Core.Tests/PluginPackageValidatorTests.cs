using SyntheticEnterprise.Contracts.Plugins;
using SyntheticEnterprise.Core.Plugins;

namespace SyntheticEnterprise.Core.Tests;

public sealed class PluginPackageValidatorTests
{
    [Fact]
    public void Validator_Reports_Missing_Root_And_Invalid_Plugins()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"datagen-plugin-missing-{Guid.NewGuid():N}");
        var tempRoot = Path.Combine(Path.GetTempPath(), $"datagen-plugin-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "broken.generator.json"), "{ not-json");

            var securityPolicy = new DataOnlyGenerationPluginSecurityPolicy();
            var catalog = new FileSystemExternalGenerationPluginCatalog(
                new GenerationPluginManifestValidator(securityPolicy),
                securityPolicy,
                new AllowListExternalPluginTrustPolicy());
            var validator = new GenerationPluginPackageValidator(catalog);

            var reports = validator.Validate(
                new[] { missingRoot, tempRoot },
                new ExternalPluginExecutionSettings());

            var missing = Assert.Single(reports, report => string.Equals(report.RootPath, Path.GetFullPath(missingRoot), StringComparison.OrdinalIgnoreCase));
            Assert.True(missing.HasErrors);
            Assert.Contains(missing.Messages, message => message.Contains("does not exist", StringComparison.OrdinalIgnoreCase));

            var invalid = Assert.Single(reports, report => string.Equals(report.RootPath, Path.GetFullPath(tempRoot), StringComparison.OrdinalIgnoreCase));
            Assert.True(invalid.HasErrors);
            Assert.Equal(1, invalid.PluginCount);
            Assert.Equal(0, invalid.ParsedCount);
            Assert.Contains(invalid.Plugins[0].ValidationMessages, message => message.Contains("could not be parsed", StringComparison.OrdinalIgnoreCase));
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
