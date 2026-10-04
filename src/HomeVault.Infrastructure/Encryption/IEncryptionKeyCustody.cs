namespace HomeVault.Infrastructure.Encryption;

/// <summary>Trusted Infrastructure boundary; no production implementation is installed by #63.</summary>
/// <remarks>
/// Issuance must generate a fresh key, serialize durable ring/recovery publication across writers,
/// verify recovery, then transfer exclusive ownership to one session. Never reactivate imported keys.
/// A failed publication returns no session. #64 must prove these requirements before host registration.
/// </remarks>
internal interface IEncryptionKeyCustody
{
    WriteKeySession? CreateVerifiedWriteSession();
    /// <summary>Returns an independently owned decrypt-only lease or null; disposal must not modify the ring.</summary>
    ReadKeyLease? FindReadKey(Guid keyId);
}
