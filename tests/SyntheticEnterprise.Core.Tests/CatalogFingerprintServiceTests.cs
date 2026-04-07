using System;
using System.IO;
using SyntheticEnterprise.Core.Services;
using Xunit;

namespace SyntheticEnterprise.Core.Tests;

public sealed class CatalogFingerprintServiceTests
{
    [Fact]
    public void Compute_is_stable_for_unchanged_catalogs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"se-catalogs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "users.csv"), "Id,Name
1,Alice
2,Bob
");
        File.WriteAllText(Path.Combine(root, "devices.csv"), "Id,Hostname
1,WS-001
");

        try
        {
            var service = new CatalogFingerprintService();
            var first = service.Compute(root);
            var second = service.Compute(root);

            Assert.Equal(first.AggregateSha256, second.AggregateSha256);
            Assert.Equal(2, first.Files.Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
