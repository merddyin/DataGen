using SyntheticEnterprise.Contracts;

namespace SyntheticEnterprise.Services;

public interface IRegenerationPlanner
{
    LayerRegenerationPolicy CreatePolicy(string layerName, string regenerationMode);
}
