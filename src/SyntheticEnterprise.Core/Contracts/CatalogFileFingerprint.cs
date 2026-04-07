using System;

namespace SyntheticEnterprise.Core.Contracts;

public sealed class CatalogFileFingerprint
{
    public string RelativePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public string SchemaSignature { get; set; } = string.Empty;
}
