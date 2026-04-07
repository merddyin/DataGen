using System.Collections.Generic;

namespace SyntheticEnterprise.Exporting.Services;

public interface ILinkTableProvider
{
    IReadOnlyList<object> GetDescriptors();
}
