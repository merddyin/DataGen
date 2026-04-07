using SyntheticEnterprise.Core.Contracts;

namespace SyntheticEnterprise.Core.Services;

public interface ICatalogFingerprintService
{
    CatalogContentFingerprint Compute(string catalogRootPath);
}
