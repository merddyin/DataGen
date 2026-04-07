using SyntheticEnterprise.Contracts;

namespace SyntheticEnterprise.Services;

/// <summary>
/// Conservative scaffold. The real implementation should inspect the actual world graph,
/// repair known foreign-key style references, and record warnings for anything unresolved.
/// </summary>
public sealed class ReferenceRepairService : IReferenceRepairService
{
    public ReferenceRepairResult RepairReferences(object world, EntityRemappingSet remapping, string layerName)
    {
        var result = new ReferenceRepairResult();

        foreach (var record in remapping.Records)
        {
            if (record.NewId is null)
            {
                result.Warnings.Add($"No replacement target exists for {record.EntityType}:{record.OldId} during {layerName} repair.");
            }
        }

        return result;
    }
}
