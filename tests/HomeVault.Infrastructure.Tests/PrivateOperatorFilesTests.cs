using System.Security.AccessControl;
using System.Security.Principal;
using HomeVault.Infrastructure.Identity;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
[Platform("Win")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class PrivateOperatorFilesTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        if (!OperatingSystem.IsWindows()) return;
        _directory = Path.Combine(Path.GetTempPath(), "HomeVault-private-" + Guid.NewGuid().ToString("N"));
        PrivateOperatorFiles.InitializeDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public async Task ExportHasPrivateOwnershipBeforeSecretsAndNeverOverwrites()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_directory, "credential.json");
        using (var stream = PrivateOperatorFiles.ReserveExport(path))
        {
            var acl = stream.GetAccessControl();
            using var identity = WindowsIdentity.GetCurrent();
            Assert.That(acl.GetOwner(typeof(SecurityIdentifier)), Is.EqualTo(identity.User));
            Assert.That(acl.AreAccessRulesProtected, Is.True);
            Assert.That(stream.Length, Is.Zero);
            await PrivateOperatorFiles.WriteAsync(stream, new AccountCredential
            { Purpose = "invitation", Login = "fictional@example.invalid", Secret = "fictional-test-only", Expires = DateTimeOffset.UtcNow });
        }
        var before = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => PrivateOperatorFiles.ReserveExport(path));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
    }

    [Test]
    public void UnsafeExportIsRejectedAndLeaseExcludesOtherHosts()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_directory, "database.db");
        using (PrivateOperatorFiles.ReserveExport(path)) { }
        using (PrivateOperatorFiles.AcquireDatabase(path))
            Assert.Throws<IOException>(() => PrivateOperatorFiles.AcquireDatabase(path));
        using (PrivateOperatorFiles.AcquireDatabase(path)) { }
        var directory = new DirectoryInfo(_directory);
        var acl = directory.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        directory.SetAccessControl(acl);
        Assert.Throws<InvalidOperationException>(() => PrivateOperatorFiles.ReserveExport(Path.Combine(_directory, "rejected.json")));
        Assert.That(File.Exists(Path.Combine(_directory, "rejected.json")), Is.False);
    }
}
