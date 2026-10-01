using HomeVault.Infrastructure.Identity;

namespace HomeVault.Playground;

internal static class SessionKeyJourney
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3 || args[1] is not ("initialize" or "check" or "renew"))
        {
            Console.Error.WriteLine("Usage: session-keys initialize|check|renew <absolute-directory>");
            return 2;
        }
        try
        {
            if (args[1] == "initialize") WindowsSessionKeys.Initialize(args[2]);
            else if (args[1] == "renew") WindowsSessionKeys.Renew(args[2]);
            using var keys = WindowsSessionKeys.Open(args[2]);
            Console.WriteLine("Session key configuration verified for the current Windows user.");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Session key operation failed. Check Windows support, private directory, account, and key lifetime.");
            return 1;
        }
    }
}
