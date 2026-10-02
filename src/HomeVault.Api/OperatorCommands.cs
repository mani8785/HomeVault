using System.Text;
using HomeVault.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace HomeVault.Api;

internal static class OperatorCommands
{
    internal static async Task<int> RunAsync(string[] args, IDataProtectionProvider keys)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (Console.IsInputRedirected || Console.IsOutputRedirected) throw new InvalidOperationException();
        var services = new ServiceCollection();
        services.AddHomeVaultAccounts(args[1], keys);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountOperations>();
        await accounts.ValidateStorageAsync();
        if (args is ["invalidate-restored", _, _])
            await accounts.InvalidateRestoredStateAsync();
        else if (args is ["disable", _, _])
        {
            var login = ReadLogin();
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<HomeVaultUser>>().FindByNameAsync(login);
            if (user is null || !await accounts.RevokeAsync(user.Id, disable: true)) throw new InvalidOperationException();
        }
        else if (args.Length == 4 && args[0] is "invite" or "recover")
        {
            using var export = PrivateOperatorFiles.ReserveExport(args[3]);
            var login = ReadLogin();
            var credential = await accounts.IssueAsync(login, args[0] == "recover") ?? throw new InvalidOperationException();
            await PrivateOperatorFiles.WriteAsync(export, credential);
        }
        else throw new InvalidOperationException();
        Console.WriteLine("Operator operation completed. Protect any exported file and transfer it only through a trusted encrypted channel.");
        return 0;
    }

    private static string ReadLogin()
    {
        Console.Write("Recipient login (input hidden): ");
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Escape) throw new InvalidOperationException();
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; continue; }
            if (char.IsControl(key.KeyChar)) continue;
            if (value.Length >= 256) throw new InvalidOperationException();
            value.Append(key.KeyChar);
        }
    }
}
