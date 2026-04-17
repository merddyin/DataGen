using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SyntheticEnterprise.Core.Contracts;

namespace SyntheticEnterprise.Core.Serialization;

public sealed class SnapshotSerializer : ISnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public void Save(object payload, string path, bool compress)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var envelopeType = payload.GetType();
        if (!envelopeType.IsGenericType || envelopeType.GetGenericTypeDefinition() != typeof(SnapshotEnvelope<>))
        {
            throw new InvalidDataException("Payload must already be wrapped in a SnapshotEnvelope.");
        }

        envelopeType.GetProperty(nameof(SnapshotEnvelope<object>.IsCompressed))?.SetValue(payload, compress);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        if (compress)
        {
            using var file = File.Create(path);
            using var gzip = new GZipStream(file, CompressionLevel.SmallestSize, leaveOpen: false);
            JsonSerializer.Serialize(gzip, payload, envelopeType, Options);
            return;
        }

        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, payload, envelopeType, Options);
    }

    public T Load<T>(string path)
    {
        using var file = File.OpenRead(path);
        Stream source = file;

        if (LooksLikeGzip(file) || Path.GetExtension(path).Equals(".gz", System.StringComparison.OrdinalIgnoreCase))
        {
            file.Position = 0;
            source = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
        }
        else
        {
            file.Position = 0;
        }

        var result = JsonSerializer.Deserialize<T>(source, Options);
        return result ?? throw new InvalidDataException("Snapshot could not be deserialized.");
    }

    private static bool LooksLikeGzip(FileStream file)
    {
        if (file.Length < 2)
        {
            return false;
        }

        var b1 = file.ReadByte();
        var b2 = file.ReadByte();
        return b1 == 0x1F && b2 == 0x8B;
    }
}
