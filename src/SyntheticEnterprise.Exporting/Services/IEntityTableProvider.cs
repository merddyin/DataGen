using System.Collections.Generic;

namespace SyntheticEnterprise.Exporting.Services;

public interface IEntityTableProvider
{
    IReadOnlyList<object> GetDescriptors();
}
