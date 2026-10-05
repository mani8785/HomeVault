using System.Security.Cryptography;
using System.Text;

namespace HomeVault.Infrastructure.Encryption;

/// <summary>Internal envelope service; deliberately absent from normal host composition until custody and authorization exist.</summary>
internal sealed class EnvelopeEncryption(IEncryptionKeyCustody custody)
{
    private readonly IEncryptionKeyCustody _custody = custody ?? throw new ArgumentNullException(nameof(custody));
    internal EncryptionResult<WriteKeySession> BeginWrite()
    {
        if (!AesGcm.IsSupported) return EncryptionResult<WriteKeySession>.Failure(EncryptionError.UnsupportedPlatform);
        try
        {
            var session = _custody.CreateVerifiedWriteSession();
            return session is null ? EncryptionResult<WriteKeySession>.Failure(EncryptionError.CustodyUnavailable) : EncryptionResult<WriteKeySession>.Success(session);
        }
        catch (PlatformNotSupportedException) { return EncryptionResult<WriteKeySession>.Failure(EncryptionError.UnsupportedPlatform); }
        catch (Exception error) when (CustodyFailure(error)) { return EncryptionResult<WriteKeySession>.Failure(EncryptionError.CustodyUnavailable); }
    }
    internal EncryptionResult<string> Decrypt(EncryptionContext context, ReadOnlySpan<byte> envelope)
    {
        if (!context.IsValid) return EncryptionResult<string>.Failure(EncryptionError.InvalidContext);
        // Snapshot bounded untrusted input before validation/key lookup to prevent concurrent caller mutation.
        if (envelope.Length > EnvelopeFormat.HeaderLength + EnvelopeFormat.MaxPlaintextBytes + EnvelopeFormat.TagLength)
            return EncryptionResult<string>.Failure(EncryptionError.TooLarge);
        var snapshot = envelope.ToArray();
        var validation = EnvelopeFormat.Validate(snapshot, out var keyId, out var length);
        if (validation != EncryptionError.None) return EncryptionResult<string>.Failure(validation);
        if (!AesGcm.IsSupported) return EncryptionResult<string>.Failure(EncryptionError.UnsupportedPlatform);
        ReadKeyLease? key;
        try { key = _custody.FindReadKey(keyId); }
        catch (PlatformNotSupportedException) { return EncryptionResult<string>.Failure(EncryptionError.UnsupportedPlatform); }
        catch (Exception error) when (CustodyFailure(error)) { return EncryptionResult<string>.Failure(EncryptionError.CustodyUnavailable); }
        if (key is null) return EncryptionResult<string>.Failure(EncryptionError.MissingKey);
        using (key)
        {
            if (key.Id != keyId) return EncryptionResult<string>.Failure(EncryptionError.CustodyUnavailable);
            var plaintext = new byte[length];
            try
            {
                var associated = EnvelopeFormat.AssociatedData(snapshot.AsSpan(0, EnvelopeFormat.HeaderLength), context);
                key.Decrypt(snapshot.AsSpan(21, 12), snapshot.AsSpan(EnvelopeFormat.HeaderLength, length), snapshot.AsSpan(EnvelopeFormat.HeaderLength + length, EnvelopeFormat.TagLength), plaintext, associated);
                return EncryptionResult<string>.Success(EnvelopeFormat.Utf8.GetString(plaintext));
            }
            catch (AuthenticationTagMismatchException) { return EncryptionResult<string>.Failure(EncryptionError.AuthenticationFailed); }
            catch (DecoderFallbackException) { return EncryptionResult<string>.Failure(EncryptionError.InvalidText); }
            catch (PlatformNotSupportedException) { return EncryptionResult<string>.Failure(EncryptionError.UnsupportedPlatform); }
            catch (ObjectDisposedException) { return EncryptionResult<string>.Failure(EncryptionError.CustodyUnavailable); }
            catch (CryptographicException) { return EncryptionResult<string>.Failure(EncryptionError.AuthenticationFailed); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
    }
    private static bool CustodyFailure(Exception error) => error is CryptographicException or IOException or UnauthorizedAccessException or InvalidOperationException;
}
