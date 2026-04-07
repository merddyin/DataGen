using System.Collections.Generic;

namespace SyntheticEnterprise.Exporting.Profiles;

public sealed class NormalizedEntityTableProvider : SyntheticEnterprise.Exporting.Services.IEntityTableProvider
{
    public IReadOnlyList<object> GetDescriptors()
    {
        // Merge-ready placeholder descriptors. Wire these to the real world model during integration.
        return [];
    }
}
