using System.Security.Cryptography;

namespace HomeVault.Infrastructure.Encryption;

/// <summary>Safe operator outcomes, without paths, secret values or provider exception details.</summary>
public enum KeyOperationOutcome
{
    /// <summary>The requested publication or verification succeeded.</summary>
    Succeeded,
    /// <summary>Windows protection is required.</summary>
    UnsupportedPlatform,
    /// <summary>Configuration, permissions, input or authentication failed; preserve known-good artifacts.</summary>
    Failed
}

/// <summary>Explicit Windows key-ring operator operations; no database configuration or Sensitive API is enabled.</summary>
public static class EncryptionKeyOperations
{
    /// <summary>Creates a new ring and a verified encrypted export without issuing a write session.</summary>
    /// <param name="directory">New absolute private ring location outside source control.</param><param name="exports">Existing owner-only export directory.</param>
    /// <param name="secret">Random 32-byte recovery secret; caller clears its own buffer.</param><returns>A safe outcome.</returns>
    public static KeyOperationOutcome Initialize(string directory, string exports, byte[] secret) => Run(() =>
    {
        if (OperatingSystem.IsWindows()) WindowsKeyCustody.Initialize(directory, exports, secret);
    });
    /// <summary>Checks that an authenticated export matches the current protected ring.</summary>
    /// <param name="directory">Existing ring.</param><param name="export">Owner-only export file.</param><param name="secret">Random recovery secret; never a password.</param><returns>A safe outcome.</returns>
    public static KeyOperationOutcome Verify(string directory, string export, byte[] secret) => Run(() =>
    {
        if (OperatingSystem.IsWindows()) WindowsKeyCustody.Verify(directory, export, secret);
    });
    /// <summary>Recovers retained keys into a new ring for the current Windows user, without enabling writes.</summary>
    /// <param name="export">Authenticated export file.</param><param name="directory">New ring destination; existing locations are never replaced.</param>
    /// <param name="secret">Separately held recovery secret.</param><returns>A safe outcome.</returns>
    public static KeyOperationOutcome Recover(string export, string directory, byte[] secret) => Run(() =>
    {
        if (OperatingSystem.IsWindows()) WindowsKeyCustody.Recover(export, directory, secret);
    });
    private static KeyOperationOutcome Run(Action operation)
    {
        if (!OperatingSystem.IsWindows()) return KeyOperationOutcome.UnsupportedPlatform;
        try { operation(); return KeyOperationOutcome.Succeeded; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or ArgumentException or System.Security.SecurityException)
        { return KeyOperationOutcome.Failed; }
    }
}
