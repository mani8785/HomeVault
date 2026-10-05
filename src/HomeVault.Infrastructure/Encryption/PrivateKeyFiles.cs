using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace HomeVault.Infrastructure.Encryption;

[SupportedOSPlatform("windows")]
internal static class PrivateKeyFiles
{
    internal static string PathChecked(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new InvalidOperationException();
        var full = Path.GetFullPath(path);
        for (var node = new DirectoryInfo(full); node is not null; node = node.Parent)
        {
            if ((Directory.Exists(node.FullName) || File.Exists(node.FullName)) && (File.GetAttributes(node.FullName) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException();
            if (Directory.Exists(Path.Combine(node.FullName, ".git")) || File.Exists(Path.Combine(node.FullName, ".git"))) throw new InvalidOperationException();
        }
        return full;
    }
    internal static void DirectoryChecked(string path)
    {
        var directory = new DirectoryInfo(PathChecked(path)); if (!directory.Exists) throw new InvalidOperationException(); Validate(directory.GetAccessControl());
    }
    internal static void CreateDirectory(string path)
    {
        path = PathChecked(path);
        if (Directory.Exists(path) || File.Exists(path) || !Directory.Exists(Path.GetDirectoryName(path))) throw new InvalidOperationException();
        using var identity = WindowsIdentity.GetCurrent(); var security = new DirectorySecurity();
        security.SetOwner(identity.User!); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(security); DirectoryChecked(path);
    }
    internal static void SecureNewFile(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(); var security = new FileSecurity();
        security.SetOwner(identity.User!); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
    internal static void FileChecked(string path)
    {
        var file = new FileInfo(PathChecked(path)); if (!file.Exists) throw new InvalidOperationException(); Validate(file.GetAccessControl());
    }
    private static void Validate(FileSystemSecurity security)
    {
        using var identity = WindowsIdentity.GetCurrent(); var user = identity.User!;
        if (!security.AreAccessRulesProtected || !user.Equals(security.GetOwner(typeof(SecurityIdentifier)))) throw new InvalidOperationException();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if (!rules.Any(rule => rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference.Equals(user) && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl) ||
            rules.Any(rule => rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(user))) throw new InvalidOperationException();
    }
    internal static byte[] Read(string path, int maximum = RecoveryPackage.Limit)
    {
        FileChecked(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maximum) throw new InvalidOperationException();
        var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); return bytes;
    }
    internal static void WriteNew(string path, byte[] bytes)
    {
        PathChecked(path); DirectoryChecked(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        SecureNewFile(path); file.Write(bytes); file.Flush(true);
    }
    internal static FileStream Lock(string directory)
    {
        DirectoryChecked(directory); var path = Path.Combine(directory, "writer.lock");
        // The initialized lock file is never deleted; all writers contend on the same OS object.
        FileChecked(path);
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 300) { Thread.Sleep(10); }
        }
    }
}
