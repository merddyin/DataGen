namespace SyntheticEnterprise.Core.Generation;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

internal static class StableHash
{
    public static int GetIndex(string domain, int count, params string?[] components)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        if (count == 1)
        {
            return 0;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "SyntheticEnterprise.StableHash/v1");
        AppendString(hash, domain);
        AppendInt32(hash, components.Length);
        foreach (var component in components)
        {
            if (component is null)
            {
                hash.AppendData([0]);
                continue;
            }

            hash.AppendData([1]);
            AppendString(hash, component);
        }

        var digest = hash.GetHashAndReset();
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(digest) % (uint)count);
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
