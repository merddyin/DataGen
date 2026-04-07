using System;
using System.Collections.Generic;

namespace SyntheticEnterprise.Core.Contracts;

public sealed class CatalogContentFingerprint
{
    public string RootPath { get; set; } = string.Empty;
    public string AggregateSha256 { get; set; } = string.Empty;
    public DateTime ComputedUtc { get; set; } = DateTime.UtcNow;
    public List<CatalogFileFingerprint> Files { get; set; } = new();
}
