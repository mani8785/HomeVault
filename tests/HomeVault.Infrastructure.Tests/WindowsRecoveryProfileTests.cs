using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using HomeVault.Infrastructure.Encryption;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class WindowsRecoveryProfileTests
{
    [Test, Platform("Win")]
    public void CurrentUserProtectionAndCiFreshProfileRecovery()
    {
        if (!OperatingSystem.IsWindows()) return;
        var purpose = "HomeVault.DataKeys.v1"u8.ToArray();
        using var ring = new KeyRingPayload(Guid.NewGuid(), 1);
        var id = Guid.NewGuid(); ring.Keys.Add(id, RandomNumberGenerator.GetBytes(32));
        var encoded = ring.Encode();
        var protectedBytes = ProtectedData.Protect(encoded, purpose, DataProtectionScope.CurrentUser);
        var roundTrip = ProtectedData.Unprotect(protectedBytes, purpose, DataProtectionScope.CurrentUser);
        Assert.That(roundTrip, Is.EqualTo(encoded));
        CryptographicOperations.ZeroMemory(encoded); CryptographicOperations.ZeroMemory(roundTrip);
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
        {
            TestContext.Out.WriteLine("Same-user DPAPI verified locally; fresh-profile case requires the disposable Windows CI account.");
            return;
        }
        var user = Environment.GetEnvironmentVariable("HOMEVAULT_TEST_USER");
        var password = Environment.GetEnvironmentVariable("HOMEVAULT_TEST_PASSWORD");
        Assert.That(user, Is.Not.Null.And.Not.Empty);
        Assert.That(password, Is.Not.Null.And.Not.Empty);
        Assert.That(LogonUser(user!, ".", password!, 2, 0, out var token), Is.True, "Disposable account logon failed.");
        using (token)
        {
            var profile = new ProfileInfo { Size = Marshal.SizeOf<ProfileInfo>(), Flags = 1, UserName = user! };
            Assert.That(LoadUserProfile(token, ref profile), Is.True, "Disposable profile could not be loaded.");
            try
            {
                using var identity = new WindowsIdentity(token.DangerousGetHandle());
                using var originalIdentity = WindowsIdentity.GetCurrent();
                Assert.That(identity.User, Is.Not.EqualTo(originalIdentity.User));
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "HomeVaultRecovery-" + Guid.NewGuid().ToString("N"));
                var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(originalIdentity.User!);
                acl.AddAccessRule(new FileSystemAccessRule(originalIdentity.User!, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                acl.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(root).Create(acl);
                var secret = RandomNumberGenerator.GetBytes(32);
                var package = RecoveryPackage.Seal(ring, secret);
                var context = new EncryptionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                using var session = new WriteKeySession(id, ring.Keys[id].ToArray());
                var ciphertext = session.Encrypt(context, "fictional portable record").ReadValue();
                try
                {
                    WindowsIdentity.RunImpersonated(token, () =>
                    {
                        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException();
                        Assert.That(() => { if (OperatingSystem.IsWindows()) ProtectedData.Unprotect(protectedBytes, purpose, DataProtectionScope.CurrentUser); }, Throws.InstanceOf<CryptographicException>());
                        var exportDirectory = Path.Combine(root, "import"); PrivateKeyFiles.CreateDirectory(exportDirectory);
                        var export = Path.Combine(exportDirectory, "fictional.hvkr"); PrivateKeyFiles.WriteNew(export, package);
                        var destination = Path.Combine(root, "restored");
                        try
                        {
                            Assert.That(EncryptionKeyOperations.Recover(export, destination, secret), Is.EqualTo(KeyOperationOutcome.Succeeded));
                            using var custody = new WindowsKeyCustody(destination);
                            Assert.That(new EnvelopeEncryption(custody).Decrypt(context, ciphertext).ReadValue(), Is.EqualTo("fictional portable record"));
                            Assert.That(custody.CreateVerifiedWriteSession(), Is.Null);
                        }
                        finally
                        {
                            if (Directory.Exists(destination)) Directory.Delete(destination, true);
                            Directory.Delete(exportDirectory, true);
                        }
                    });
                }
                finally { CryptographicOperations.ZeroMemory(secret); Directory.Delete(root, false); }
            }
            finally { Assert.That(UnloadUserProfile(token, profile.Profile), Is.True, "Disposable profile did not unload."); }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProfileInfo
    {
        public int Size;
        public int Flags;
        public string UserName;
        public string? ProfilePath;
        public string? DefaultPath;
        public string? ServerName;
        public string? PolicyPath;
        public IntPtr Profile;
    }

    [DllImport("advapi32.dll", EntryPoint = "LogonUserW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string user, string domain, string password, int type, int provider, out SafeAccessTokenHandle token);

    [DllImport("userenv.dll", EntryPoint = "LoadUserProfileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LoadUserProfile(SafeAccessTokenHandle token, ref ProfileInfo profile);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnloadUserProfile(SafeAccessTokenHandle token, IntPtr profile);
}
