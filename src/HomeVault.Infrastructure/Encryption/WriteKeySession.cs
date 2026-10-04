using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HomeVault.Infrastructure.Encryption;

/// <summary>Exclusive process-local write key with bounded, nonrefundable nonce reservations.</summary>
/// <remarks>Only verified custody may issue a session. This internal constructor also supports isolated test custody.</remarks>
internal sealed class WriteKeySession : IDisposable
{
    internal const int ReservationLimit = 65_536;
    private readonly byte[] _key;
    private readonly Guid _id;
    private readonly Action<byte[]> _fillNonce;
    private readonly HashSet<(ulong, uint)> _nonces = [];
    private readonly object _sync = new();
    private bool _disposed;
    /// <summary>Takes exclusive ownership of a fresh 32-byte key after durable verified recovery publication.</summary>
    internal WriteKeySession(Guid id, byte[] ownedKey, Action<byte[]>? testEntropy = null)
    {
        ArgumentNullException.ThrowIfNull(ownedKey);
        if (id == Guid.Empty || ownedKey.Length != 32) throw new ArgumentException("Invalid key configuration.");
        _id = id; _key = ownedKey; _fillNonce = testEntropy ?? (buffer => RandomNumberGenerator.Fill(buffer));
    }
    internal EncryptionResult<byte[]> Encrypt(EncryptionContext context, string? text)
    {
        lock (_sync)
        {
            if (_disposed) return EncryptionResult<byte[]>.Failure(EncryptionError.SessionDisposed);
            if (!context.IsValid) return EncryptionResult<byte[]>.Failure(EncryptionError.InvalidContext);
            if (!AesGcm.IsSupported) return EncryptionResult<byte[]>.Failure(EncryptionError.UnsupportedPlatform);
            if (_nonces.Count >= ReservationLimit) return EncryptionResult<byte[]>.Failure(EncryptionError.SessionExhausted);
            byte[]? plaintext = null;
            try
            {
                var nonce = new byte[12]; var reserved = false;
                // One initial attempt and at most eight retries. Reservations are never refunded.
                for (var attempt = 0; attempt < 9; attempt++)
                {
                    _fillNonce(nonce);
                    if (_nonces.Add((BinaryPrimitives.ReadUInt64BigEndian(nonce), BinaryPrimitives.ReadUInt32BigEndian(nonce.AsSpan(8))))) { reserved = true; break; }
                }
                if (!reserved) return EncryptionResult<byte[]>.Failure(EncryptionError.NonceUnavailable);
                if (text is null) return EncryptionResult<byte[]>.Failure(EncryptionError.InvalidText);
                if (text.Length > EnvelopeFormat.MaxPlaintextBytes || EnvelopeFormat.Utf8.GetByteCount(text) > EnvelopeFormat.MaxPlaintextBytes)
                    return EncryptionResult<byte[]>.Failure(EncryptionError.TooLarge);
                plaintext = EnvelopeFormat.Utf8.GetBytes(text);
                var envelope = EnvelopeFormat.Allocate(_id, nonce, plaintext.Length);
                var associated = EnvelopeFormat.AssociatedData(envelope.AsSpan(0, EnvelopeFormat.HeaderLength), context);
                using var cipher = new AesGcm(_key, EnvelopeFormat.TagLength);
                cipher.Encrypt(nonce, plaintext, envelope.AsSpan(EnvelopeFormat.HeaderLength, plaintext.Length), envelope.AsSpan(EnvelopeFormat.HeaderLength + plaintext.Length, EnvelopeFormat.TagLength), associated);
                return EncryptionResult<byte[]>.Success(envelope);
            }
            catch (EncoderFallbackException) { return EncryptionResult<byte[]>.Failure(EncryptionError.InvalidText); }
            catch (PlatformNotSupportedException) { return EncryptionResult<byte[]>.Failure(EncryptionError.UnsupportedPlatform); }
            catch (CryptographicException) { return EncryptionResult<byte[]>.Failure(EncryptionError.EncryptionFailed); }
            finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; CryptographicOperations.ZeroMemory(_key); _nonces.Clear();
        }
    }
    public override string ToString() => nameof(WriteKeySession);
}
