using SyntheticEnterprise.Module.Contracts;

namespace SyntheticEnterprise.Module.Services;

public interface IGoldenFileComparer
{
    GoldenComparisonResult CompareDirectory(string baselineName, string actualPath);
}
