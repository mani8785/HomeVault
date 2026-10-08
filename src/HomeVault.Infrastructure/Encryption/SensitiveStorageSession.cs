using HomeVault.Application.Assets;
using HomeVault.Infrastructure.Persistence;

namespace HomeVault.Infrastructure.Encryption;

/// <summary>Owns an explicitly unlocked, bounded host write session and retained-key reads.</summary>
/// <remarks>Dispose after all requests finish. No automatic key renewal or plaintext fallback is supported.</remarks>
public sealed class SensitiveStorageSession : IDisposable
{
    private readonly IEncryptionKeyCustody _custody;
    private readonly EnvelopeEncryption _encryption;
    private readonly WriteKeySession _writer;
    private bool _disposed;
    internal SensitiveStorageSession(IEncryptionKeyCustody custody)
    {
        _custody = custody; _encryption = new EnvelopeEncryption(custody);
        var opened = _encryption.BeginWrite();
        if (!opened.Succeeded) throw new InvalidOperationException("Sensitive storage is unavailable.");
        _writer = opened.ReadValue();
    }

    /// <summary>Publishes and verifies a fresh Windows write key using an explicitly supplied recovery secret.</summary>
    /// <param name="ringDirectory">Existing private data ring.</param><param name="exportDirectory">Separate private export directory.</param>
    /// <param name="secret">Random recovery secret; caller clears its own buffer.</param>
    /// <returns>An owned host session; retained keys cannot be reactivated for writes.</returns>
    /// <exception cref="PlatformNotSupportedException">The host is not Windows.</exception>
    /// <exception cref="InvalidOperationException">Custody or recovery publication failed; details are omitted.</exception>
    public static SensitiveStorageSession OpenWindows(string ringDirectory, string exportDirectory, byte[] secret)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        WindowsKeyCustody? custody = null;
        try { custody = new WindowsKeyCustody(ringDirectory, exportDirectory, secret); return new SensitiveStorageSession(custody); }
        catch { if (OperatingSystem.IsWindows()) custody?.Dispose(); throw new InvalidOperationException("Sensitive storage is unavailable."); }
    }

    /// <summary>Creates a purpose-specific adapter without accessing a database.</summary>
    /// <param name="database">The explicitly migrated SQLite configuration.</param><returns>An authorized encrypted-attribute store.</returns>
    /// <exception cref="ArgumentNullException">Database is null.</exception>
    /// <exception cref="ObjectDisposedException">This host session has ended.</exception>
    public ISensitiveAttributeStore CreateStore(SqliteDatabase database)
    { ArgumentNullException.ThrowIfNull(database); ObjectDisposedException.ThrowIf(_disposed, this); return new SqliteSensitiveAttributeStore(database, this); }

    internal EncryptionResult<byte[]> Encrypt(EncryptionContext context, string value) => _writer.Encrypt(context, value);
    internal EncryptionResult<string> Decrypt(EncryptionContext context, byte[] envelope) => _disposed
        ? EncryptionResult<string>.Failure(EncryptionError.CustodyUnavailable) : _encryption.Decrypt(context, envelope);
    /// <summary>Clears owned write-key and recovery-secret buffers; repeated disposal is harmless.</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; _writer.Dispose(); (_custody as IDisposable)?.Dispose(); }
    /// <summary>Returns a fixed label without keys or configuration.</summary><returns>A safe label.</returns>
    public override string ToString() => nameof(SensitiveStorageSession);
}
