using System.Security.Cryptography;
using HomeVault.Infrastructure.Encryption;
using HomeVault.Playground;

namespace HomeVault.Api;

internal static class EncryptionMaintenanceCommands
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected ||
            !(args is ["rotate-encryption" or "backup-encrypted", _, _, _] or ["recover-encrypted", _, _])) throw new InvalidOperationException();
        Console.WriteLine("All hosts and database tools must be stopped. Enter the separately held random recovery key.");
        var secret = EncryptionKeyJourney.ReadSecret();
        try
        {
            var result = args[0] switch
            {
                "rotate-encryption" => await EncryptedMaintenance.RotateAsync(args[1], args[2], args[3], secret),
                "backup-encrypted" => await EncryptedMaintenance.BackupAsync(args[1], args[2], args[3], secret),
                "recover-encrypted" => await EncryptedMaintenance.RecoverAsync(args[1], args[2], secret),
                _ => throw new InvalidOperationException()
            };
            Console.WriteLine(result == KeyOperationOutcome.Succeeded
                ? "Maintenance completed. Recovery requires deliberate activation; no host was started."
                : "Maintenance failed. Preserve existing stores and backups; rotation may have completed some batches. No credentials were printed.");
            return result == KeyOperationOutcome.Succeeded ? 0 : 1;
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
}
