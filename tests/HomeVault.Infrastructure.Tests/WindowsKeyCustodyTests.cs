using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using HomeVault.Infrastructure.Encryption;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class WindowsKeyCustodyTests
{
    private string _root = null!;
    private string Ring => Path.Combine(_root, "ring");
    private string Exports => Path.Combine(_root, "exports");
    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(32);
    private static readonly EncryptionContext Context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "HomeVaultTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        if (OperatingSystem.IsWindows()) PrivateKeyFiles.CreateDirectory(Exports);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public void UnsupportedPlatformsDoNotCreateArtifacts()
    {
        if (OperatingSystem.IsWindows())
            Assert.That(EncryptionKeyOperations.Initialize("relative", Exports, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        else
        {
            Assert.That(EncryptionKeyOperations.Initialize(Ring, Exports, _secret), Is.EqualTo(KeyOperationOutcome.UnsupportedPlatform));
            Assert.That(EncryptionKeyOperations.Verify(Ring, "missing", _secret), Is.EqualTo(KeyOperationOutcome.UnsupportedPlatform));
            Assert.That(EncryptionKeyOperations.Recover("missing", Ring, _secret), Is.EqualTo(KeyOperationOutcome.UnsupportedPlatform));
        }
        Assert.That(Directory.Exists(Ring), Is.False);
    }

    [Test, Platform("Win")]
    public void RecoveryRetainsKeysAndNewSessionsUseFreshKeys()
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowsKeyCustody.Initialize(Ring, Exports, _secret);
        using var custody = new WindowsKeyCustody(Ring, Exports, _secret);
        var service = new EnvelopeEncryption(custody);
        using var first = service.BeginWrite().ReadValue();
        var ciphertext = first.Encrypt(Context, "fictional private value").ReadValue();
        using var second = service.BeginWrite().ReadValue();
        using var ring = WindowsKeyCustody.Load(Ring, out var generation);
        Assert.That(ring.Keys.Count, Is.EqualTo(2));
        Assert.That(ring.Generation, Is.EqualTo(2));
        Assert.That(ring.Keys.Values.First(), Is.Not.EqualTo(ring.Keys.Values.Last()));
        var export = Directory.GetFiles(Exports, $"*-{generation}.hvkr").Single();
        Assert.That(EncryptionKeyOperations.Verify(Ring, export, _secret), Is.EqualTo(KeyOperationOutcome.Succeeded));
        var restored = Path.Combine(_root, "restored");
        Assert.That(EncryptionKeyOperations.Recover(export, restored, _secret), Is.EqualTo(KeyOperationOutcome.Succeeded));
        using var reader = new WindowsKeyCustody(restored);
        Assert.That(reader.CreateVerifiedWriteSession(), Is.Null);
        Assert.That(new EnvelopeEncryption(reader).Decrypt(Context, ciphertext).ReadValue(), Is.EqualTo("fictional private value"));
        Assert.That(EncryptionKeyOperations.Recover(export, restored, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        Assert.That(EncryptionKeyOperations.Initialize(Ring, Exports, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        foreach (var path in Directory.GetFiles(Ring)) PrivateKeyFiles.FileChecked(path);
    }

    [TestCase("protected"), TestCase("export"), TestCase("committed"), Platform("Win")]
    public void InterruptedPublicationNeverIssuesAKeyAndRetainsPriorReads(string checkpoint)
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowsKeyCustody.Initialize(Ring, Exports, _secret);
        using var original = new WindowsKeyCustody(Ring, Exports, _secret);
        using var session = original.CreateVerifiedWriteSession()!;
        var ciphertext = session.Encrypt(Context, "fictional").ReadValue();
        using var failing = new WindowsKeyCustody(Ring, Exports, _secret, point => { if (point == checkpoint) throw new IOException(); });
        Assert.That(new EnvelopeEncryption(failing).BeginWrite().Succeeded, Is.False);
        Assert.That(new EnvelopeEncryption(original).Decrypt(Context, ciphertext).ReadValue(), Is.EqualTo("fictional"));
        using var current = WindowsKeyCustody.Load(Ring, out var generation);
        Assert.That(current.Keys.Count, Is.EqualTo(checkpoint == "committed" ? 2 : 1));
        var export = Directory.GetFiles(Exports, $"*-{generation}.hvkr").Single();
        WindowsKeyCustody.Verify(Ring, export, _secret);
    }

    [Test, Platform("Win")]
    public async Task IndependentCustodiansSerializePublicationWithoutLosingKeys()
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowsKeyCustody.Initialize(Ring, Exports, _secret);
        using var first = new WindowsKeyCustody(Ring, Exports, _secret);
        using var second = new WindowsKeyCustody(Ring, Exports, _secret);
        await Task.WhenAll(Task.Run(() => { if (OperatingSystem.IsWindows()) first.CreateVerifiedWriteSession()!.Dispose(); }), Task.Run(() => { if (OperatingSystem.IsWindows()) second.CreateVerifiedWriteSession()!.Dispose(); }));
        using var ring = WindowsKeyCustody.Load(Ring, out _);
        Assert.That(ring.Keys.Count, Is.EqualTo(2));
        Assert.That(ring.Generation, Is.EqualTo(2));
        Assert.That(Directory.GetFiles(Exports, "*.hvkr"), Has.Length.EqualTo(3));
    }

    [Test, Platform("Win")]
    public void CollisionsMissingArtifactsAndCheckoutPathsAreRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowsKeyCustody.Initialize(Ring, Exports, _secret);
        var file = Path.Combine(Exports, "existing.hvkr"); PrivateKeyFiles.WriteNew(file, [7, 8]);
        Assert.Throws<IOException>(() => { if (OperatingSystem.IsWindows()) PrivateKeyFiles.WriteNew(file, [9]); });
        Assert.That(File.ReadAllBytes(file), Is.EqualTo(new byte[] { 7, 8 }));
        Assert.Throws<InvalidOperationException>(() => { if (OperatingSystem.IsWindows()) PrivateKeyFiles.CreateDirectory(Exports); });
        var checkout = Path.Combine(_root, "checkout"); Directory.CreateDirectory(Path.Combine(checkout, ".git"));
        Assert.That(EncryptionKeyOperations.Initialize(Path.Combine(checkout, "ring"), Exports, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        File.Delete(Path.Combine(Ring, "current"));
        Assert.That(EncryptionKeyOperations.Verify(Ring, file, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        Assert.That(File.Exists(Path.Combine(Ring, "current")), Is.False);
    }

    [Test, Platform("Win")]
    public void WrongSecretCorruptionAndUnsafePermissionsFailWithoutRepair()
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowsKeyCustody.Initialize(Ring, Exports, _secret);
        var pointer = File.ReadAllBytes(Path.Combine(Ring, "current"));
        using var wrong = new WindowsKeyCustody(Ring, Exports, RandomNumberGenerator.GetBytes(32));
        Assert.That(new EnvelopeEncryption(wrong).BeginWrite().Succeeded, Is.False);
        Assert.That(File.ReadAllBytes(Path.Combine(Ring, "current")), Is.EqualTo(pointer));
        var export = Directory.GetFiles(Exports).Single();
        Assert.That(EncryptionKeyOperations.Recover(export, Path.Combine(_root, "wrong"), new byte[32]), Is.EqualTo(KeyOperationOutcome.Failed));
        Assert.That(Directory.Exists(Path.Combine(_root, "wrong")), Is.False);
        var security = new DirectoryInfo(Ring).GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        new DirectoryInfo(Ring).SetAccessControl(security);
        Assert.That(EncryptionKeyOperations.Verify(Ring, export, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        security.RemoveAccessRuleAll(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        new DirectoryInfo(Ring).SetAccessControl(security);
        var data = Directory.GetFiles(Ring, "*.dpapi").Single();
        File.WriteAllBytes(data, [1, 2, 3]);
        Assert.That(EncryptionKeyOperations.Verify(Ring, export, _secret), Is.EqualTo(KeyOperationOutcome.Failed));
        Assert.That(File.ReadAllBytes(data), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }
}
