using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace HomeVault.Infrastructure.Encryption;

/// <summary>Windows-only internal custody; writes require an explicitly supplied, owned recovery-secret copy.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsKeyCustody : IEncryptionKeyCustody, IDisposable
{
    private static readonly byte[] Purpose = "HomeVault.DataKeys.v1"u8.ToArray();
    private readonly string _directory;
    private readonly string? _exports;
    private readonly byte[]? _secret;
    private readonly Action<string>? _testCheckpoint;
    private readonly object _sync = new();
    private bool _disposed;
    internal WindowsKeyCustody(string directory, string? exports = null, byte[]? secret = null, Action<string>? testCheckpoint = null)
    {
        PrivateKeyFiles.DirectoryChecked(directory); _directory = directory;
        if (secret is not null && (secret.Length != 32 || exports is null)) throw new InvalidOperationException();
        if (exports is not null) { PrivateKeyFiles.DirectoryChecked(exports); Separate(directory, exports); }
        using var ring = Load(directory, out _);
        _exports = exports; _secret = secret?.ToArray(); _testCheckpoint = testCheckpoint;
    }
    public WriteKeySession? CreateVerifiedWriteSession()
    {
        lock (_sync)
        {
            if (_disposed || _secret is null || _exports is null) return null;
            using var fileLock = PrivateKeyFiles.Lock(_directory);
            using var ring = Load(_directory, out var generation);
            using (var recovery = RecoveryPackage.Open(PrivateKeyFiles.Read(Path.Combine(_directory, generation + ".recovery")), _secret))
                if (!RecoveryPackage.Equal(ring, recovery)) throw new InvalidOperationException();
            if (ring.Keys.Count >= KeyRingPayload.MaximumKeys || ring.Generation == ulong.MaxValue) return null;
            var id = Guid.NewGuid();
            if (ring.Keys.ContainsKey(id)) throw new InvalidOperationException();
            ring.Keys.Add(id, RandomNumberGenerator.GetBytes(32)); ring.Generation++;
            Publish(_directory, _exports, ring, _secret, _testCheckpoint);
            return new WriteKeySession(id, ring.Keys[id].ToArray());
        }
    }
    public ReadKeyLease? FindReadKey(Guid keyId)
    {
        lock (_sync)
        {
            if (_disposed) return null;
            using var ring = Load(_directory, out _);
            return ring.Keys.TryGetValue(keyId, out var key) ? new ReadKeyLease(keyId, key.ToArray()) : null;
        }
    }
    internal static void Initialize(string directory, string exports, ReadOnlySpan<byte> secret)
    {
        if (secret.Length != 32) throw new InvalidOperationException();
        PrivateKeyFiles.PathChecked(directory); PrivateKeyFiles.DirectoryChecked(exports); Separate(directory, exports);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException();
        var staging = directory + ".provision-" + Guid.NewGuid().ToString("N"); PrivateKeyFiles.CreateDirectory(staging);
        PrivateKeyFiles.WriteNew(Path.Combine(staging, "writer.lock"), []);
        using var ring = new KeyRingPayload(Guid.NewGuid(), 0);
        Publish(staging, exports, ring, secret, null);
        Directory.Move(staging, directory);
    }
    internal static void Verify(string directory, string export, ReadOnlySpan<byte> secret)
    {
        using var ring = Load(directory, out _);
        using var recovered = RecoveryPackage.Open(PrivateKeyFiles.Read(export), secret);
        if (!RecoveryPackage.Equal(ring, recovered)) throw new InvalidOperationException();
    }
    internal static void Recover(string export, string directory, ReadOnlySpan<byte> secret)
    {
        PrivateKeyFiles.PathChecked(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException();
        var package = PrivateKeyFiles.Read(export);
        using var ring = RecoveryPackage.Open(package, secret);
        var staging = directory + ".recover-" + Guid.NewGuid().ToString("N"); PrivateKeyFiles.CreateDirectory(staging);
        PrivateKeyFiles.WriteNew(Path.Combine(staging, "writer.lock"), []);
        var generation = Guid.NewGuid().ToString("N"); StoreProtected(staging, generation, ring);
        PrivateKeyFiles.WriteNew(Path.Combine(staging, generation + ".recovery"), package);
        Commit(staging, generation);
        using (var restored = Load(staging, out _))
        {
            if (!RecoveryPackage.Equal(ring, restored)) throw new InvalidOperationException();
            // Synthetic proof of recovered key usability. No real record or database is published here.
            if (restored.Keys.Count != 0)
            {
                var first = restored.Keys.First(); var salt = RandomNumberGenerator.GetBytes(32);
                var originalCheckKey = new byte[32]; var recoveredCheckKey = new byte[32];
                try
                {
                    // Never encrypt with a retained data key: derive one-use, purpose-separated check keys.
                    HKDF.DeriveKey(HashAlgorithmName.SHA256, ring.Keys[first.Key], originalCheckKey, salt, "HomeVault.RecoveryCheck.v1"u8);
                    HKDF.DeriveKey(HashAlgorithmName.SHA256, first.Value, recoveredCheckKey, salt, "HomeVault.RecoveryCheck.v1"u8);
                    var tag = new byte[16]; using var originalCipher = new AesGcm(originalCheckKey, 16); using var recoveredCipher = new AesGcm(recoveredCheckKey, 16);
                    originalCipher.Encrypt(new byte[12], ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag);
                    recoveredCipher.Decrypt(new byte[12], ReadOnlySpan<byte>.Empty, tag, Span<byte>.Empty);
                }
                finally { CryptographicOperations.ZeroMemory(originalCheckKey); CryptographicOperations.ZeroMemory(recoveredCheckKey); }
            }
        }
        Directory.Move(staging, directory);
    }
    private static void Separate(string first, string second)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)); var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
    }
    private static void Publish(string directory, string exports, KeyRingPayload ring, ReadOnlySpan<byte> secret, Action<string>? checkpoint)
    {
        var generation = Guid.NewGuid().ToString("N"); StoreProtected(directory, generation, ring); checkpoint?.Invoke("protected");
        var package = RecoveryPackage.Seal(ring, secret);
        var local = Path.Combine(directory, generation + ".recovery"); PrivateKeyFiles.WriteNew(local, package);
        var export = Path.Combine(exports, $"{ring.Id:N}-{ring.Generation}-{generation}.hvkr"); PrivateKeyFiles.WriteNew(export, package);
        using (var verified = RecoveryPackage.Open(PrivateKeyFiles.Read(export), secret))
            if (!RecoveryPackage.Equal(ring, verified)) throw new InvalidOperationException();
        using (var verified = RecoveryPackage.Open(PrivateKeyFiles.Read(local), secret))
            if (!RecoveryPackage.Equal(ring, verified)) throw new InvalidOperationException();
        checkpoint?.Invoke("export"); Commit(directory, generation); checkpoint?.Invoke("committed");
    }
    private static void StoreProtected(string directory, string generation, KeyRingPayload ring)
    {
        var plaintext = ring.Encode(); byte[]? verified = null;
        try
        {
            var path = Path.Combine(directory, generation + ".dpapi");
            PrivateKeyFiles.WriteNew(path, ProtectedData.Protect(plaintext, Purpose, DataProtectionScope.CurrentUser));
            verified = ProtectedData.Unprotect(PrivateKeyFiles.Read(path), Purpose, DataProtectionScope.CurrentUser);
            if (!CryptographicOperations.FixedTimeEquals(plaintext, verified)) throw new InvalidOperationException();
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); if (verified is not null) CryptographicOperations.ZeroMemory(verified); }
    }
    private static void Commit(string directory, string generation)
    {
        var temporary = Path.Combine(directory, generation + ".pointer"); PrivateKeyFiles.WriteNew(temporary, System.Text.Encoding.ASCII.GetBytes(generation));
        var current = Path.Combine(directory, "current");
        if (File.Exists(current)) { PrivateKeyFiles.FileChecked(current); File.Replace(temporary, current, null); }
        else File.Move(temporary, current);
        PrivateKeyFiles.FileChecked(current);
    }
    internal static KeyRingPayload Load(string directory, out string generation)
    {
        PrivateKeyFiles.DirectoryChecked(directory);
        var bytes = PrivateKeyFiles.Read(Path.Combine(directory, "current"), 32);
        generation = System.Text.Encoding.ASCII.GetString(bytes);
        if (bytes.Length != 32 || !Guid.TryParseExact(generation, "N", out _)) throw new InvalidOperationException();
        var plaintext = ProtectedData.Unprotect(PrivateKeyFiles.Read(Path.Combine(directory, generation + ".dpapi")), Purpose, DataProtectionScope.CurrentUser);
        try { return KeyRingPayload.Decode(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public void Dispose() { lock (_sync) { _disposed = true; if (_secret is not null) CryptographicOperations.ZeroMemory(_secret); } }
}
