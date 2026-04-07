using System.Collections.Generic;
using System.IO;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Writers;
using Xunit;

namespace SyntheticEnterprise.Exporting.Tests;

public sealed class JsonArtifactWriterTests
{
    [Fact]
    public void Write_Creates_Json_File()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            var writer = new JsonArtifactWriter();
            var rows = new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["group_id"] = "G001", ["display_name"] = "All Employees" }
            };

            var artifact = writer.Write(temp, "directory_groups", new[] { "group_id", "display_name" }, rows, ExportArtifactKind.EntityTable);
            var path = Path.Combine(temp, artifact.RelativePath);

            Assert.True(File.Exists(path));
            Assert.Equal("application/json", artifact.MediaType);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }
}
