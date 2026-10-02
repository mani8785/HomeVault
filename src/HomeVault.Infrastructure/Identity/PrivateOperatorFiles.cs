using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace HomeVault.Infrastructure.Identity;

/// <summary>Windows-only private local operator paths, exports and host exclusion.</summary>
[SupportedOSPlatform("windows")]
public static class PrivateOperatorFiles
{
    /// <summary>Creates a new private directory outside checkouts with explicit current-user ownership.</summary>
    /// <param name="path">New absolute directory with an existing parent.</param>
    /// <exception cref="InvalidOperationException">The path or existing destination is unsafe.</exception>
    public static void InitializeDirectory(string path)
    {
        var directory = PathDirectory(path);
        if (directory.Exists || File.Exists(path) || directory.Parent?.Exists != true) throw new InvalidOperationException();
        using var identity = WindowsIdentity.GetCurrent();
        var security = new DirectorySecurity();
        security.SetOwner(identity.User!);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.Create(security);
    }

    /// <summary>Holds an exclusive file handle shared by all supported server/operator entry points.</summary>
    /// <param name="databasePath">Existing database in a private directory outside source control.</param>
    /// <returns>A lease that must live until the host or operator stops.</returns>
    /// <exception cref="IOException">Another process holds the lease or storage is inaccessible.</exception>
    /// <exception cref="InvalidOperationException">Database paths or permissions are unsafe.</exception>
    public static FileStream AcquireDatabase(string databasePath)
    {
        if (!Path.IsPathFullyQualified(databasePath)) throw new InvalidOperationException();
        var file = new FileInfo(databasePath);
        ValidateDirectory(file.Directory!.FullName);
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
        ValidatePermissions(file.GetAccessControl());
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        {
            var sidecar = new FileInfo(databasePath + suffix);
            if (!sidecar.Exists) continue;
            if ((sidecar.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
            ValidatePermissions(sidecar.GetAccessControl());
        }
        var lockFile = new FileInfo(databasePath + ".host-lock");
        if (lockFile.Exists && (lockFile.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
        // Persistent lock file: deleting it would permit two different handles to bypass exclusion.
        return new FileStream(lockFile.FullName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Reserves an empty private export before issuing any credential, without overwriting.</summary>
    /// <param name="path">New file under an explicitly provisioned private directory.</param>
    /// <returns>A private writable stream; the caller disposes it after export.</returns>
    /// <exception cref="IOException">The destination already exists or cannot be created.</exception>
    /// <exception cref="InvalidOperationException">The directory is unsafe.</exception>
    public static FileStream ReserveExport(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException();
        var file = new FileInfo(path);
        ValidateDirectory(file.Directory!.FullName);
        using var identity = WindowsIdentity.GetCurrent();
        var security = new FileSecurity();
        security.SetOwner(identity.User!);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
        return file.Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, security);
    }

    /// <summary>Writes the bearer credential only to an already reserved private stream.</summary>
    /// <param name="stream">Stream returned by ReserveExport.</param>
    /// <param name="credential">Secret export; never pass it to logs or normal console output.</param>
    /// <returns>A task completing after bytes are flushed to disk.</returns>
    public static async Task WriteAsync(FileStream stream, AccountCredential credential)
    {
        await JsonSerializer.SerializeAsync(stream, credential);
        stream.Flush(flushToDisk: true);
    }

    private static void ValidateDirectory(string path)
    {
        var directory = PathDirectory(path);
        if (!directory.Exists) throw new InvalidOperationException();
        var security = directory.GetAccessControl();
        if (!security.AreAccessRulesProtected) throw new InvalidOperationException();
        ValidatePermissions(security);
    }

    private static DirectoryInfo PathDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException();
        var directory = new DirectoryInfo(path);
        if (directory.Parent is null) throw new InvalidOperationException();
        for (var ancestor = directory; ancestor is not null; ancestor = ancestor.Parent)
            if ((ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) ||
                File.Exists(Path.Combine(ancestor.FullName, ".git")) || Directory.Exists(Path.Combine(ancestor.FullName, ".git")))
                throw new InvalidOperationException();
        return directory;
    }

    private static void ValidatePermissions(FileSystemSecurity security)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.User!.Equals(security.GetOwner(typeof(SecurityIdentifier)))) throw new InvalidOperationException();
        var allowed = false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (!rule.IdentityReference.Equals(identity.User)) throw new InvalidOperationException();
            allowed = true;
        }
        if (!allowed) throw new InvalidOperationException();
    }
}
