using SyntheticEnterprise.Core.Contracts;
using SyntheticEnterprise.Core.Services;
using Xunit;

namespace SyntheticEnterprise.Core.Tests;

public sealed class SchemaCompatibilityServiceTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", CompatibilityLevel.Compatible)]
    [InlineData("1.0.0", "1.4.2", CompatibilityLevel.CompatibleWithWarnings)]
    [InlineData("1.0.0", "2.0.0", CompatibilityLevel.Incompatible)]
    public void Assess_returns_expected_level(string currentVersion, string importedVersion, CompatibilityLevel expected)
    {
        var service = new SchemaCompatibilityService();
        var assessment = service.Assess(currentVersion, importedVersion);
        Assert.Equal(expected, assessment.Level);
    }
}
