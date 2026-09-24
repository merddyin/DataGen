using SyntheticEnterprise.Services;
using Xunit;

namespace SyntheticEnterprise.Tests;

public sealed class LayerMergeServiceTests
{
    [Fact]
    public void MergeLayer_ReturnsConservativeMergedOutcome()
    {
        var service = new LayerMergeService();
        var policy = new RegenerationPlanner().CreatePolicy("Identity", "Merge");

        var result = service.MergeLayer(new object(), new object(), policy);

        Assert.Equal("Identity", result.LayerName);
        Assert.Equal("Merge", result.RegenerationMode);
        Assert.NotEmpty(result.Warnings);
    }
}
