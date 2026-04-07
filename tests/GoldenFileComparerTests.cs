using SyntheticEnterprise.Module.Services;

namespace SyntheticEnterprise.Tests;

public sealed class GoldenFileComparerTests
{
    [Fact]
    public void Normalizer_Should_Collapse_Line_Endings_And_Path_Separators()
    {
        var normalizer = new DeterministicValueNormalizer();
        var result = normalizer.Normalize("c:\\\\temp\\out\r\nline2");
        Assert.Equal("c://temp/out\nline2", result);
    }
}
