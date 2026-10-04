namespace HomeVault.Infrastructure.Encryption;

/// <summary>Internal safe failures; a later authorized Application boundary must avoid exposing a key oracle.</summary>
internal enum EncryptionError
{
    None, InvalidContext, InvalidPayload, UnsupportedVersion, InvalidText, TooLarge,
    MissingKey, AuthenticationFailed, CustodyUnavailable, UnsupportedPlatform,
    SessionExhausted, NonceUnavailable, SessionDisposed, EncryptionFailed
}

/// <summary>Explicit success or safe failure; payload is never a serializable property.</summary>
internal sealed class EncryptionResult<T> where T : class
{
    private readonly T? _value;
    private EncryptionResult(T? value, EncryptionError error) { _value = value; Error = error; }
    public EncryptionError Error { get; }
    public bool Succeeded => Error == EncryptionError.None;
    internal static EncryptionResult<T> Success(T value) => new(value, EncryptionError.None);
    internal static EncryptionResult<T> Failure(EncryptionError error) => new(null, error);
    /// <summary>Deliberately obtains a successful result; plaintext strings cannot be securely erased.</summary>
    internal T ReadValue() => _value ?? throw new InvalidOperationException("Encryption result is unavailable.");
    public override string ToString() => nameof(EncryptionResult<T>);
}

/// <summary>Stable record identity supplied by an already authorized caller; labels are never identity.</summary>
internal readonly record struct EncryptionContext(Guid VaultId, Guid AssetId, Guid AttributeId)
{
    internal bool IsValid => VaultId != Guid.Empty && AssetId != Guid.Empty && AttributeId != Guid.Empty;
    public override string ToString() => nameof(EncryptionContext);
}
