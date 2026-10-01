using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace HomeVault.Infrastructure.Identity;

/// <summary>Explicitly provisions and opens Windows-user-protected session keys.</summary>
/// <remarks>
/// Uses framework Data Protection with DPAPI CurrentUser and a private directory.
/// This protects authentication cookies/tokens, not Asset values. The caller owns
/// this object's lifetime. Never delete old keys while sessions or tokens need them.
/// Directory and key validity are checked at open; already opened providers may cache keys.
/// </remarks>
public sealed class WindowsSessionKeys : IDataProtectionProvider, IDisposable
{
    private readonly ServiceProvider _services;
    private readonly IDataProtectionProvider _provider;

    private WindowsSessionKeys(ServiceProvider services)
    {
        _services = services;
        _provider = services.GetRequiredService<IDataProtectionProvider>();
    }

    /// <summary>Creates a new private key directory and its first 90-day session key.</summary>
    /// <param name="path">New absolute directory outside any Git checkout; its parent must exist.</param>
    /// <exception cref="PlatformNotSupportedException">The operating system is not Windows.</exception>
    /// <exception cref="InvalidOperationException">Provisioning fails; no existing directory is overwritten.</exception>
    public static void Initialize(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Session key protection requires Windows.");
        string? staging = null;
        try
        {
            var directory = ValidatePath(path);
            if (Directory.Exists(directory.FullName) || File.Exists(directory.FullName)) throw new InvalidOperationException();
            if (directory.Parent is null || !directory.Parent.Exists) throw new InvalidOperationException();
            var security = new DirectorySecurity();
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User!;
            security.SetOwner(user);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            var temporary = new DirectoryInfo(directory.FullName + ".provision-" + Guid.NewGuid().ToString("N"));
            temporary.Create(security);
            staging = temporary.FullName;
            using (var services = Build(temporary))
            {
                var now = DateTimeOffset.UtcNow;
                services.GetRequiredService<IKeyManager>().CreateNewKey(now, now.AddDays(90));
                ValidateRing(temporary, services, requireActive: true);
            }
            Directory.Move(staging, directory.FullName);
            staging = null;
        }
        catch (Exception exception) when (IsConfigurationFailure(exception))
        {
            throw new InvalidOperationException("Session key provisioning failed. Use a new private directory with an existing parent.");
        }
        finally
        {
            if (staging is not null)
            {
                // Only remove the uniquely named unpublished directory created above.
                try { Directory.Delete(staging, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Opens an existing usable key ring; never creates replacement keys.</summary>
    /// <param name="path">Absolute private directory provisioned for the current Windows user.</param>
    /// <returns>A provider to register at the host composition boundary and dispose on shutdown.</returns>
    /// <exception cref="PlatformNotSupportedException">The operating system is not Windows.</exception>
    /// <exception cref="InvalidOperationException">Keys are missing, expired, unreadable, unprotected, or permissions are unsafe.</exception>
    public static WindowsSessionKeys Open(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Session key protection requires Windows.");
        ServiceProvider? services = null;
        try
        {
            var directory = ValidatePath(path);
            services = Build(directory);
            ValidateRing(directory, services, requireActive: true);
            return new WindowsSessionKeys(services);
        }
        catch (Exception exception) when (IsConfigurationFailure(exception))
        {
            services?.Dispose();
            throw new InvalidOperationException("Session keys are unavailable. Check provisioning, permissions, account, and key lifetime.");
        }
    }

    /// <summary>Explicitly adds a new 90-day key while retaining all old keys.</summary>
    /// <param name="path">Existing private ring, including one whose usable keys have expired.</param>
    /// <remarks>Stop hosts before renewal and restart them afterwards. Renewal does not revoke prior tokens.</remarks>
    /// <exception cref="PlatformNotSupportedException">The operating system is not Windows.</exception>
    /// <exception cref="InvalidOperationException">The existing ring cannot be validated or updated.</exception>
    public static void Renew(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Session key protection requires Windows.");
        try
        {
            var directory = ValidatePath(path);
            using var services = Build(directory);
            ValidateRing(directory, services, requireActive: false);
            var now = DateTimeOffset.UtcNow;
            services.GetRequiredService<IKeyManager>().CreateNewKey(now, now.AddDays(90));
            ValidateRing(directory, services, requireActive: true);
        }
        catch (Exception exception) when (IsConfigurationFailure(exception))
        {
            throw new InvalidOperationException("Session key renewal failed. Preserve the existing ring and check its configuration.");
        }
    }

    /// <summary>Creates a framework protector isolated by purpose within HomeVault sessions.</summary>
    /// <param name="purpose">Stable non-secret protocol purpose; use distinct purposes for different protocols.</param>
    /// <returns>A framework data protector.</returns>
    public IDataProtector CreateProtector(string purpose) => _provider.CreateProtector(purpose);

    /// <summary>Releases owned Data Protection services after the host stops using them.</summary>
    public void Dispose() => _services.Dispose();

    [SupportedOSPlatform("windows")]
    private static ServiceProvider Build(DirectoryInfo directory)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("HomeVault.Sessions.v1")
            .PersistKeysToFileSystem(directory).ProtectKeysWithDpapi(protectToLocalMachine: false)
            .DisableAutomaticKeyGeneration();
        return services.BuildServiceProvider();
    }

    private static DirectoryInfo ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new InvalidOperationException();
        var directory = new DirectoryInfo(path);
        if (directory.Parent is null) throw new InvalidOperationException();
        for (var ancestor = directory; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (File.Exists(Path.Combine(ancestor.FullName, ".git")) || Directory.Exists(Path.Combine(ancestor.FullName, ".git")))
                throw new InvalidOperationException();
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
        }
        return directory;
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateRing(DirectoryInfo directory, ServiceProvider services, bool requireActive)
    {
        if (!directory.Exists) throw new InvalidOperationException();
        ValidatePermissions(directory.GetAccessControl());
        if (!directory.GetAccessControl().AreAccessRulesProtected) throw new InvalidOperationException();
        var files = directory.GetFiles("*.xml");
        if (files.Length == 0) throw new InvalidOperationException();
        foreach (var file in files)
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > 1024 * 1024) throw new InvalidOperationException();
            ValidatePermissions(file.GetAccessControl());
            var xml = XElement.Load(file.FullName);
            if (xml.Name == "key" && (xml.Descendants("masterKey").Any() ||
                !xml.Descendants(XName.Get("encryptedSecret", "http://schemas.asp.net/2015/03/dataProtection")).Any(element =>
                    ((string?)element.Attribute("decryptorType"))?.StartsWith(typeof(DpapiXmlDecryptor).FullName + ",", StringComparison.Ordinal) == true)))
                throw new InvalidOperationException();
        }
        var keys = services.GetRequiredService<IKeyManager>().GetAllKeys();
        var now = DateTimeOffset.UtcNow;
        if (keys.Count == 0 || !keys.Any(key => !key.IsRevoked && (!requireActive ||
            key.ActivationDate <= now && key.ExpirationDate > now))) throw new InvalidOperationException();
        foreach (var key in keys.Where(key => !key.IsRevoked))
            if (key.CreateEncryptor() is null) throw new InvalidOperationException();
    }

    [SupportedOSPlatform("windows")]
    private static void ValidatePermissions(FileSystemSecurity security)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        if (!user.Equals(security.GetOwner(typeof(SecurityIdentifier)))) throw new InvalidOperationException();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
        if (!rules.Any(rule => rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference.Equals(user)))
            throw new InvalidOperationException();
        if (rules.Any(rule => rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(user)))
            throw new InvalidOperationException();
    }

    private static bool IsConfigurationFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or ArgumentException or XmlException;

}
