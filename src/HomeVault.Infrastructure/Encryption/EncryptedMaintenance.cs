using System.Runtime.Versioning;
using System.Security.Cryptography;
using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeVault.Infrastructure.Encryption;

/// <summary>Offline Windows rotation and coordinated recovery without plaintext exports or automatic activation.</summary>
/// <remarks>Stop all hosts and other database tools. Supported commands share exclusion locks; arbitrary SQLite clients do not.</remarks>
public static class EncryptedMaintenance
{
    /// <summary>Re-encrypts all Sensitive records in bounded transactions, retaining all old keys.</summary>
    /// <param name="database">Existing private database.</param><param name="ring">Existing private data ring.</param>
    /// <param name="exports">Separate existing private recovery-export directory.</param><param name="secret">Random 32-byte recovery key; caller clears its buffer after completion.</param>
    /// <param name="token">Cancellation rolls back the current batch; earlier batches remain readable.</param>
    /// <returns>A safe outcome; failure can leave completed batches and additional retained keys. Rerun to finish.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static Task<KeyOperationOutcome> RotateAsync(string database, string ring, string exports, byte[] secret, CancellationToken token = default) =>
        Run(() => RotateCore(database, ring, exports, secret, token));

    /// <summary>Publishes a new authenticated database/key recovery set after full validation.</summary>
    /// <param name="database">Existing private database.</param><param name="ring">Existing private data ring.</param>
    /// <param name="destination">New recovery-set directory with an existing parent; never overwritten.</param><param name="secret">Separately held random recovery key.</param>
    /// <param name="token">Cancellation prevents publication.</param><returns>A safe outcome, without diagnostic or secret details.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static Task<KeyOperationOutcome> BackupAsync(string database, string ring, string destination, byte[] secret, CancellationToken token = default) =>
        Run(() => BackupCore(database, ring, destination, secret, token));

    /// <summary>Restores a verified set into new private stores and invalidates historical sessions and outstanding credentials.</summary>
    /// <param name="source">Private directory containing the fixed database, recovery export and manifest files.</param>
    /// <param name="destination">New destination root with an existing parent; no live configuration is changed.</param>
    /// <param name="secret">Separately held recovery key; never a password.</param><param name="token">Cancellation prevents publication.</param>
    /// <returns>A safe outcome. Success requires deliberate host activation with the new paths.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static Task<KeyOperationOutcome> RecoverAsync(string source, string destination, byte[] secret, CancellationToken token = default) =>
        Run(() => RecoverCore(source, destination, secret, token));

    private static async Task<KeyOperationOutcome> Run(Func<Task> action)
    {
        if (!OperatingSystem.IsWindows()) return KeyOperationOutcome.UnsupportedPlatform;
        try { await action(); return KeyOperationOutcome.Succeeded; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or ArgumentException or
            System.Security.SecurityException or SqliteException or DbUpdateException or OverflowException)
        { return KeyOperationOutcome.Failed; }
    }

    internal static async Task RotateCore(string database, string ring, string exports, byte[] secret, CancellationToken token, Action<string>? checkpoint = null, int sessionLimit = WriteKeySession.ReservationLimit)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Separate(Path.GetDirectoryName(database)!, ring, exports);
        using var hostLock = PrivateOperatorFiles.AcquireDatabase(database);
        using var ringLock = PrivateKeyFiles.Lock(ring);
        using var custody = new WindowsKeyCustody(ring, exports, secret, checkpoint);
        await EncryptedDatabaseMaintenance.Rotate(new SqliteDatabase(database), custody, custody.CreateWriteSessionUnderMaintenanceLock, token, checkpoint, sessionLimit);
    }

    internal static async Task BackupCore(string database, string ring, string destination, byte[] secret, CancellationToken token, Action<string>? checkpoint = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Separate(Path.GetDirectoryName(database)!, ring, destination);
        NewDestination(destination);
        using var hostLock = PrivateOperatorFiles.AcquireDatabase(database);
        using var ringLock = PrivateKeyFiles.Lock(ring);
        using var custody = new WindowsKeyCustody(ring);
        using var payload = WindowsKeyCustody.Load(ring, out var generation);
        var export = PrivateKeyFiles.Read(Path.Combine(ring, generation + ".recovery"));
        using (var verified = RecoveryPackage.Open(export, secret))
            if (!RecoveryPackage.Equal(payload, verified)) throw new InvalidOperationException();
        var staging = destination + ".partial-" + Guid.NewGuid().ToString("N");
        PrivateKeyFiles.CreateDirectory(staging);
        // Leave failed staging for explicit operator inspection; never recursively delete user paths.
        var copy = Path.Combine(staging, "database.sqlite");
        await new SqliteDatabase(database).CreateVerifiedCopyAsync(copy, token);
        PrivateKeyFiles.SecureNewFile(copy);
        await EncryptedDatabaseMaintenance.Validate(new SqliteDatabase(copy), custody, token);
        FlushDatabase(copy);
        var keys = Path.Combine(staging, "keys.hvkr"); PrivateKeyFiles.WriteNew(keys, export);
        var manifest = BackupManifest.Seal(copy, keys, payload.Id, payload.Generation, secret);
        PrivateKeyFiles.WriteNew(Path.Combine(staging, "manifest.hvbm"), manifest);
        _ = VerifySet(staging, secret);
        checkpoint?.Invoke("backup-verified"); token.ThrowIfCancellationRequested();
        Directory.Move(staging, destination);
    }

    internal static async Task RecoverCore(string source, string destination, byte[] secret, CancellationToken token, Action<string>? checkpoint = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Separate(source, destination); NewDestination(destination);
        _ = VerifySet(source, secret);
        var staging = destination + ".partial-" + Guid.NewGuid().ToString("N");
        PrivateKeyFiles.CreateDirectory(staging);
        foreach (var name in new[] { "database.sqlite", "keys.hvkr", "manifest.hvbm" }) CopyPrivate(Path.Combine(source, name), Path.Combine(staging, name));
        _ = VerifySet(staging, secret); checkpoint?.Invoke("recovery-copied");
        var dataDirectory = Path.Combine(staging, "database"); PrivateKeyFiles.CreateDirectory(dataDirectory);
        var database = Path.Combine(dataDirectory, "homevault.sqlite");
        File.Move(Path.Combine(staging, "database.sqlite"), database);
        var ring = Path.Combine(staging, "data-keys"); WindowsKeyCustody.Recover(Path.Combine(staging, "keys.hvkr"), ring, secret);
        using (var custody = new WindowsKeyCustody(ring))
            await EncryptedDatabaseMaintenance.Validate(new SqliteDatabase(database), custody, token);
        var sessions = Path.Combine(staging, "session-keys"); WindowsSessionKeys.Initialize(sessions);
        using (var keys = WindowsSessionKeys.Open(sessions))
        using (var services = new ServiceCollection().AddHomeVaultAccounts(database, keys).BuildServiceProvider())
        using (var scope = services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().InvalidateRestoredStateAsync();
        checkpoint?.Invoke("recovery-invalidated");
        using (var custody = new WindowsKeyCustody(ring))
            await EncryptedDatabaseMaintenance.Validate(new SqliteDatabase(database), custody, token);
        FlushDatabase(database);
        // The original manifest describes the pre-invalidation source, not this live destination.
        File.Delete(Path.Combine(staging, "manifest.hvbm")); File.Delete(Path.Combine(staging, "keys.hvkr"));
        checkpoint?.Invoke("recovery-verified"); token.ThrowIfCancellationRequested();
        Directory.Move(staging, destination);
    }

    [SupportedOSPlatform("windows")]
    private static (Guid Id, ulong Generation) VerifySet(string directory, byte[] secret)
    {
        PrivateKeyFiles.DirectoryChecked(directory);
        var database = Path.Combine(directory, "database.sqlite"); var export = Path.Combine(directory, "keys.hvkr");
        PrivateKeyFiles.FileChecked(database); PrivateKeyFiles.FileChecked(export);
        // Sidecars must never override the authenticated database bytes.
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(database + suffix) || Directory.Exists(database + suffix)) throw new InvalidOperationException();
        var result = BackupManifest.Verify(PrivateKeyFiles.Read(Path.Combine(directory, "manifest.hvbm"), BackupManifest.Length), database, export, secret);
        using var payload = RecoveryPackage.Open(PrivateKeyFiles.Read(export), secret);
        if (payload.Id != result.Id || payload.Generation != result.Generation) throw new InvalidOperationException();
        return result;
    }
    [SupportedOSPlatform("windows")]
    private static void NewDestination(string path)
    {
        PrivateKeyFiles.PathChecked(path);
        if (Directory.Exists(path) || File.Exists(path) || !Directory.Exists(Path.GetDirectoryName(path))) throw new InvalidOperationException();
    }
    [SupportedOSPlatform("windows")]
    private static void CopyPrivate(string source, string destination)
    {
        PrivateKeyFiles.FileChecked(source);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        PrivateKeyFiles.SecureNewFile(destination); input.CopyTo(output); output.Flush(true);
    }
    private static void FlushDatabase(string path)
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=DELETE";
            if (!string.Equals(command.ExecuteScalar() as string, "delete", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        }
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); file.Flush(true);
    }
    private static void Separate(params string[] paths)
    {
        var full = paths.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).ToArray();
        for (var i = 0; i < full.Length; i++)
            for (var j = i + 1; j < full.Length; j++)
                if (full[i].Equals(full[j], StringComparison.OrdinalIgnoreCase) || full[i].StartsWith(full[j] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || full[j].StartsWith(full[i] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
    }
}
