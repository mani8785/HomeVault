using System.Buffers.Binary;
using System.Text;

namespace HomeVault.Infrastructure.Encryption;

/// <summary>Strict version-one framing and authenticated context encoding; no provider calls or secret logging.</summary>
internal static class EnvelopeFormat
{
    internal const int HeaderLength = 37;
    internal const int TagLength = 16;
    internal const int MaxPlaintextBytes = 1024 * 1024;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    private static ReadOnlySpan<byte> Purpose => "HomeVault.SensitiveAttribute\0"u8;

    internal static EncryptionError Validate(ReadOnlySpan<byte> envelope, out Guid keyId, out int length)
    {
        keyId = Guid.Empty; length = 0;
        if (envelope.Length < HeaderLength + TagLength || !envelope[..4].SequenceEqual("HVAE"u8)) return EncryptionError.InvalidPayload;
        if (envelope[4] != 1) return EncryptionError.UnsupportedVersion;
        keyId = new Guid(envelope.Slice(5, 16), bigEndian: true);
        if (keyId == Guid.Empty) return EncryptionError.InvalidPayload;
        var size = BinaryPrimitives.ReadUInt32BigEndian(envelope.Slice(33, 4));
        if (size > MaxPlaintextBytes) return EncryptionError.TooLarge;
        length = (int)size;
        return envelope.Length == HeaderLength + length + TagLength ? EncryptionError.None : EncryptionError.InvalidPayload;
    }
    internal static byte[] Allocate(Guid keyId, ReadOnlySpan<byte> nonce, int length)
    {
        var envelope = new byte[HeaderLength + length + TagLength];
        "HVAE"u8.CopyTo(envelope); envelope[4] = 1;
        keyId.TryWriteBytes(envelope.AsSpan(5, 16), bigEndian: true, out _);
        nonce.CopyTo(envelope.AsSpan(21, 12));
        BinaryPrimitives.WriteUInt32BigEndian(envelope.AsSpan(33, 4), (uint)length);
        return envelope;
    }
    internal static byte[] AssociatedData(ReadOnlySpan<byte> header, EncryptionContext context)
    {
        var data = new byte[Purpose.Length + HeaderLength + 48];
        Purpose.CopyTo(data); header.CopyTo(data.AsSpan(Purpose.Length, HeaderLength));
        var offset = Purpose.Length + HeaderLength;
        context.VaultId.TryWriteBytes(data.AsSpan(offset, 16), bigEndian: true, out _);
        context.AssetId.TryWriteBytes(data.AsSpan(offset + 16, 16), bigEndian: true, out _);
        context.AttributeId.TryWriteBytes(data.AsSpan(offset + 32, 16), bigEndian: true, out _);
        return data;
    }
}
