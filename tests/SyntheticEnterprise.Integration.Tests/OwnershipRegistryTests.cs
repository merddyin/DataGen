using SyntheticEnterprise.Services;
using Xunit;

namespace SyntheticEnterprise.Tests;

public sealed class OwnershipRegistryTests
{
    [Fact]
    public void IdentityLayer_HasOwnedArtifacts()
    {
        var registry = new DefaultLayerOwnershipRegistry();
        var owned = registry.GetOwnedArtifacts("Identity");
        Assert.NotEmpty(owned);
    }
}
