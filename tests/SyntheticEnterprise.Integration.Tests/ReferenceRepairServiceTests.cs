using SyntheticEnterprise.Contracts;
using SyntheticEnterprise.Services;
using Xunit;

namespace SyntheticEnterprise.Tests;

public sealed class ReferenceRepairServiceTests
{
    [Fact]
    public void RepairReferences_Warns_WhenReplacementTargetMissing()
    {
        var service = new ReferenceRepairService();
        var remapping = new EntityRemappingSet
        {
            LayerName = "Identity",
        };

        remapping.Records.Add(new EntityRemappingRecord
        {
            EntityType = "DirectoryAccount",
            OldId = "A1",
            NewId = null,
            RemapDisposition = "Removed",
            Reason = "Entity dropped during replace",
        });

        var result = service.RepairReferences(new object(), remapping, "Identity");
        Assert.NotEmpty(result.Warnings);
    }
}
