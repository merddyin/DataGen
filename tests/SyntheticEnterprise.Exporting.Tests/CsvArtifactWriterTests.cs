using System.Collections.Generic;
using System.IO;
using System.Text;
using SyntheticEnterprise.Exporting.Contracts;
using SyntheticEnterprise.Exporting.Writers;
using Xunit;

namespace SyntheticEnterprise.Exporting.Tests;

public sealed class CsvArtifactWriterTests
{
    [Fact]
    public void Write_Creates_Deterministic_Csv_File()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            var writer = new CsvArtifactWriter();
            var rows = new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["person_id"] = "P001", ["display_name"] = "A. User" },
                new Dictionary<string, object?> { ["person_id"] = "P002", ["display_name"] = "B. User" }
            };

            var artifact = writer.Write(temp, "people", new[] { "person_id", "display_name" }, rows, ExportArtifactKind.EntityTable);
            var path = Path.Combine(temp, artifact.RelativePath);

            Assert.True(File.Exists(path));
            Assert.Equal(2, artifact.RowCount);
            Assert.NotEmpty(artifact.Sha256);
            Assert.Equal(
                Encoding.UTF8.GetBytes("person_id,display_name\nP001,A. User\nP002,B. User\n"),
                File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Write_NormalizesEmbeddedLineEndingsToLf()
    {
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            var writer = new CsvArtifactWriter();
            var rows = new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["person_id"] = "P001", ["notes"] = "line1\r\nline2" }
            };

            var artifact = writer.Write(temp, "people", new[] { "person_id", "notes" }, rows, ExportArtifactKind.EntityTable);

            Assert.Equal(
                Encoding.UTF8.GetBytes("person_id,notes\nP001,\"line1\nline2\"\n"),
                File.ReadAllBytes(Path.Combine(temp, artifact.RelativePath)));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }
}
