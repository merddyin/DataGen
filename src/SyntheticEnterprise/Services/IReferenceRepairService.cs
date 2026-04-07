using SyntheticEnterprise.Contracts;

namespace SyntheticEnterprise.Services;

public interface IReferenceRepairService
{
    ReferenceRepairResult RepairReferences(object world, EntityRemappingSet remapping, string layerName);
}
