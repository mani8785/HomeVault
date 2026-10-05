using System.Security.Cryptography;

namespace HomeVault.Infrastructure.Encryption;

/// <summary>Owns a key buffer without exposing bytes; supports decryption cipher construction only.</summary>
internal sealed class ReadKeyLease : IDisposable
{
    private readonly byte[] _key;
    private readonly object _sync = new();
    private bool _disposed;
    /// <summary>Takes exclusive ownership of exactly 32 bytes. Caller must not retain or reuse the buffer.</summary>
    internal ReadKeyLease(Guid id, byte[] ownedKey)
    {
        ArgumentNullException.ThrowIfNull(ownedKey);
        if (id == Guid.Empty || ownedKey.Length != 32) throw new ArgumentException("Invalid key configuration.");
        Id = id; _key = ownedKey;
    }
    internal Guid Id { get; }
    internal void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var cipher = new AesGcm(_key, 16);
            cipher.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        }
    }
    public void Dispose()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; CryptographicOperations.ZeroMemory(_key); }
    }
    public override string ToString() => nameof(ReadKeyLease);
}
