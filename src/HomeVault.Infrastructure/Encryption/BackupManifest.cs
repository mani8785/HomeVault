using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HomeVault.Infrastructure.Encryption;

// HVBM v1: magic(4), version(1), salt(32), network GUID(16), generation(8),
// database length(8), export length(8), database SHA256(32), export SHA256(32), HMAC(32).
internal static class BackupManifest
{
    internal const int Length = 173;
    private const int TagOffset = 141;
    internal static byte[] Seal(string database, string export, Guid id, ulong generation, ReadOnlySpan<byte> secret, byte[]? testSalt = null)
    {
        var bytes = new byte[Length]; "HVBM"u8.CopyTo(bytes); bytes[4] = 1;
        (testSalt ?? RandomNumberGenerator.GetBytes(32)).CopyTo(bytes, 5);
        id.TryWriteBytes(bytes.AsSpan(37, 16), true, out _);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(53), generation);
        HashFile(database, bytes.AsSpan(61, 8), bytes.AsSpan(77, 32));
        HashFile(export, bytes.AsSpan(69, 8), bytes.AsSpan(109, 32));
        Tag(bytes, secret).CopyTo(bytes, TagOffset);
        return bytes;
    }
    internal static (Guid Id, ulong Generation) Verify(byte[] bytes, string database, string export, ReadOnlySpan<byte> secret)
    {
        if (bytes.Length != Length || !CryptographicOperations.FixedTimeEquals(Tag(bytes, secret), bytes.AsSpan(TagOffset))) throw new InvalidOperationException();
        if (!bytes.AsSpan(0, 4).SequenceEqual("HVBM"u8) || bytes[4] != 1) throw new InvalidOperationException();
        var id = new Guid(bytes.AsSpan(37, 16), true);
        if (id == Guid.Empty) throw new InvalidOperationException();
        VerifyFile(database, bytes.AsSpan(61, 8), bytes.AsSpan(77, 32));
        VerifyFile(export, bytes.AsSpan(69, 8), bytes.AsSpan(109, 32));
        return (id, BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(53, 8)));
    }
    private static byte[] Tag(byte[] bytes, ReadOnlySpan<byte> secret)
    {
        if (secret.Length != 32) throw new InvalidOperationException();
        Span<byte> key = stackalloc byte[32];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, key, bytes.AsSpan(5, 32), "HomeVault.BackupManifest.v1"u8);
            return HMACSHA256.HashData(key, bytes.AsSpan(0, TagOffset));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void HashFile(string path, Span<byte> length, Span<byte> hash)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BinaryPrimitives.WriteInt64BigEndian(length, file.Length);
        SHA256.HashData(file).CopyTo(hash);
    }
    private static void VerifyFile(string path, ReadOnlySpan<byte> length, ReadOnlySpan<byte> hash)
    {
        Span<byte> actualLength = stackalloc byte[8]; Span<byte> actualHash = stackalloc byte[32];
        HashFile(path, actualLength, actualHash);
        if (!actualLength.SequenceEqual(length) || !CryptographicOperations.FixedTimeEquals(actualHash, hash)) throw new InvalidOperationException();
    }
}
