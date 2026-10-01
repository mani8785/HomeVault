using System.Security.AccessControl;
using System.Security.Principal;
using System.Xml.Linq;
using HomeVault.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
public sealed class WindowsSessionKeyTests
{
    private string _directory = null!;
    private string _keys = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVaultTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _keys = Path.Combine(_directory, "keys");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public void PlatformBoundaryIsExplicit()
    {
        if (OperatingSystem.IsWindows())
            Assert.Throws<InvalidOperationException>(() => WindowsSessionKeys.Open(_keys));
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => WindowsSessionKeys.Initialize(_keys));
            Assert.Throws<PlatformNotSupportedException>(() => WindowsSessionKeys.Open(_keys));
            Assert.Throws<PlatformNotSupportedException>(() => WindowsSessionKeys.Renew(_keys));
        }
        Assert.That(Directory.Exists(_keys), Is.False);
    }

    [Test]
    [Platform("Win")]
    public void ProtectedKeysReopenAndRenewWithoutLosingOldPayloads()
    {
        WindowsSessionKeys.Initialize(_keys);
        string payload;
        using (var first = WindowsSessionKeys.Open(_keys))
            payload = first.CreateProtector("test-session").Protect("Fictional session");
        var keyFile = Directory.GetFiles(_keys, "key-*.xml").Single();
        var xml = XElement.Load(keyFile);
        Assert.That(xml.Descendants("masterKey"), Is.Empty);
        Assert.That(xml.Descendants().Count(element => element.Name.LocalName == "encryptedSecret"), Is.EqualTo(1));
        var before = File.ReadAllBytes(keyFile);
        Assert.Throws<InvalidOperationException>(() => WindowsSessionKeys.Initialize(_keys));
        Assert.That(File.ReadAllBytes(keyFile), Is.EqualTo(before));
        WindowsSessionKeys.Renew(_keys);
        Assert.That(Directory.GetFiles(_keys, "key-*.xml").Length, Is.EqualTo(2));
        using var reopened = WindowsSessionKeys.Open(_keys);
        Assert.That(reopened.CreateProtector("test-session").Unprotect(payload), Is.EqualTo("Fictional session"));
        Assert.Catch<System.Security.Cryptography.CryptographicException>(() => reopened.CreateProtector("different-purpose").Unprotect(payload));
    }

    [TestCase("missing")]
    [TestCase("corrupt")]
    [TestCase("plaintext")]
    [TestCase("ciphertext")]
    [Platform("Win")]
    public void DamagedKeysFailSafelyWithoutReplacement(string damage)
    {
        WindowsSessionKeys.Initialize(_keys);
        var file = Directory.GetFiles(_keys, "key-*.xml").Single();
        if (damage == "missing") File.Delete(file);
        else if (damage == "corrupt") File.WriteAllText(file, "Fictional invalid XML");
        else if (damage == "plaintext")
        {
            var xml = XElement.Load(file);
            xml.Descendants().Single(element => element.Name.LocalName == "encryptedSecret").ReplaceWith(new XElement("masterKey", "Fictional plaintext"));
            xml.Save(file);
        }
        else
        {
            var xml = XElement.Load(file);
            var value = xml.Descendants().Single(element => element.Name.LocalName == "encryptedKey").Element("value")!;
            value.Value = Convert.ToBase64String(new byte[32]);
            xml.Save(file);
        }
        var count = Directory.GetFiles(_keys).Length;
        var error = Assert.Throws<InvalidOperationException>(() => WindowsSessionKeys.Open(_keys));
        Assert.That(error!.ToString(), Does.Not.Contain(_keys).And.Not.Contain("Fictional"));
        Assert.Throws<InvalidOperationException>(() => WindowsSessionKeys.Renew(_keys));
        Assert.That(Directory.GetFiles(_keys).Length, Is.EqualTo(count));
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win")]
    public void BroadDirectoryOrFilePermissionsAreRejected(bool onFile)
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowsSessionKeys.Initialize(_keys);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var rule = new FileSystemAccessRule(everyone, FileSystemRights.Read, AccessControlType.Allow);
        if (onFile)
        {
            var file = new FileInfo(Directory.GetFiles(_keys, "key-*.xml").Single());
            var security = file.GetAccessControl();
            security.AddAccessRule(rule);
            file.SetAccessControl(security);
        }
        else
        {
            var directory = new DirectoryInfo(_keys);
            var security = directory.GetAccessControl();
            security.AddAccessRule(rule);
            directory.SetAccessControl(security);
        }
        Assert.Throws<InvalidOperationException>(() => WindowsSessionKeys.Open(_keys));
    }

    [Test]
    [Platform("Win")]
    public void ExpiredRingNeedsExplicitRenewalAndRetainsOldPayloads()
    {
        WindowsSessionKeys.Initialize(_keys);
        string payload;
        using (var original = WindowsSessionKeys.Open(_keys))
            payload = original.CreateProtector("expiry-test").Protect("Fictional value");
        var file = Directory.GetFiles(_keys, "key-*.xml").Single();
        var xml = XElement.Load(file);
        xml.Element("creationDate")!.Value = DateTimeOffset.UtcNow.AddDays(-100).ToString("O");
        xml.Element("activationDate")!.Value = DateTimeOffset.UtcNow.AddDays(-100).ToString("O");
        xml.Element("expirationDate")!.Value = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        xml.Save(file);
        Assert.Throws<InvalidOperationException>(() => WindowsSessionKeys.Open(_keys));
        Assert.That(Directory.GetFiles(_keys, "key-*.xml").Length, Is.EqualTo(1));
        WindowsSessionKeys.Renew(_keys);
        using var renewed = WindowsSessionKeys.Open(_keys);
        Assert.That(renewed.CreateProtector("expiry-test").Unprotect(payload), Is.EqualTo("Fictional value"));
    }

    [Test]
    [Platform("Win")]
    public void RelativeAndCheckoutPathsAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => WindowsSessionKeys.Initialize("relative-keys"));
        var checkout = Path.Combine(_directory, "checkout");
        Directory.CreateDirectory(Path.Combine(checkout, ".git"));
        Assert.Throws<InvalidOperationException>(() => WindowsSessionKeys.Initialize(Path.Combine(checkout, "keys")));
        Assert.That(Directory.Exists(Path.Combine(checkout, "keys")), Is.False);
    }
}
