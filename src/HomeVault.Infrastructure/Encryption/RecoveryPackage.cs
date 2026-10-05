using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HomeVault.Infrastructure.Encryption;

internal static class RecoveryPackage
{
    internal const int Limit = 1024 * 1024;
    private const int Header = 77;
    private static ReadOnlySpan<byte> Purpose => "HomeVault.KeyRecovery.v1\0"u8;
    internal static byte[] Seal(KeyRingPayload ring, ReadOnlySpan<byte> secret)
    {
        if (secret.Length != 32) throw new InvalidOperationException();
        var plaintext = ring.Encode(); var derived = new byte[32];
        try
        {
            var output = new byte[Header + plaintext.Length + 16];
            "HVKR"u8.CopyTo(output); output[4] = 1; ring.Id.TryWriteBytes(output.AsSpan(5, 16), true, out _);
            BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(21, 8), ring.Generation);
            RandomNumberGenerator.Fill(output.AsSpan(29, 32)); RandomNumberGenerator.Fill(output.AsSpan(61, 12));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(73, 4), (uint)plaintext.Length);
            HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, derived, output.AsSpan(29, 32), Purpose);
            using var aes = new AesGcm(derived, 16);
            aes.Encrypt(output.AsSpan(61, 12), plaintext, output.AsSpan(Header, plaintext.Length), output.AsSpan(Header + plaintext.Length, 16), Aad(output));
            return output;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); CryptographicOperations.ZeroMemory(derived); }
    }
    internal static KeyRingPayload Open(ReadOnlySpan<byte> package, ReadOnlySpan<byte> secret)
    {
        if (secret.Length != 32 || package.Length < Header + 16 || package.Length > Limit || !package[..4].SequenceEqual("HVKR"u8) || package[4] != 1)
            throw new InvalidOperationException();
        var id = new Guid(package.Slice(5, 16), true); var length = BinaryPrimitives.ReadUInt32BigEndian(package.Slice(73, 4));
        if (id == Guid.Empty || length > Limit - Header - 16 || package.Length != Header + length + 16) throw new InvalidOperationException();
        var derived = new byte[32]; var plaintext = new byte[(int)length];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, derived, package.Slice(29, 32), Purpose);
            using var aes = new AesGcm(derived, 16);
            aes.Decrypt(package.Slice(61, 12), package.Slice(Header, (int)length), package.Slice(Header + (int)length, 16), plaintext, Aad(package));
            var ring = KeyRingPayload.Decode(plaintext);
            if (ring.Id == id && ring.Generation == BinaryPrimitives.ReadUInt64BigEndian(package.Slice(21, 8))) return ring;
            ring.Dispose(); throw new InvalidOperationException();
        }
        finally { CryptographicOperations.ZeroMemory(derived); CryptographicOperations.ZeroMemory(plaintext); }
    }
    private static byte[] Aad(ReadOnlySpan<byte> header) { var bytes = new byte[Header + Purpose.Length]; header[..Header].CopyTo(bytes); Purpose.CopyTo(bytes.AsSpan(Header)); return bytes; }
    internal static bool Equal(KeyRingPayload first, KeyRingPayload second)
    {
        var left = first.Encode(); byte[]? right = null;
        try { right = second.Encode(); return CryptographicOperations.FixedTimeEquals(left, right); }
        finally { CryptographicOperations.ZeroMemory(left); if (right is not null) CryptographicOperations.ZeroMemory(right); }
    }
}
