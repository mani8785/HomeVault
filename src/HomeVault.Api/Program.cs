using HomeVault.Api;
using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Encryption;
using HomeVault.Playground;
using System.Security.Cryptography;

try
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    if (args is ["private-directory", var directory])
    {
        PrivateOperatorFiles.InitializeDirectory(directory);
        Console.WriteLine("Private directory provisioned.");
        return 0;
    }
    if (args.Length > 0 && args[0] is "rotate-encryption" or "backup-encrypted" or "recover-encrypted")
        return await EncryptionMaintenanceCommands.RunAsync(args);
    if (args.Length < 3) throw new InvalidOperationException();
    using var lease = PrivateOperatorFiles.AcquireDatabase(args[1]);
    using var keys = WindowsSessionKeys.Open(args[2]);
    using var encrypted = args[0] == "serve-encrypted" ? Unlock(args) : null;
    if ((args[0] == "serve" && args.Length == 3) || (args[0] == "serve-encrypted" && args.Length == 5))
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration["AllowedHosts"] = "localhost";
        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(7443, endpoint => endpoint.UseHttps()));
        builder.Logging.ClearProviders();
        AuthenticationHost.Configure(builder.Services, args[1], keys);
        RecordsApi.Configure(builder.Services, args[1], encrypted);
        await using var app = builder.Build();
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().ValidateStorageAsync();
        AuthenticationHost.Map(app);
        RecordsApi.Map(app, encrypted is not null);
        Console.WriteLine("HomeVault listening at https://localhost:7443. Press Ctrl+C to stop.");
        await app.RunAsync();
        return 0;
    }
    return await OperatorCommands.RunAsync(args, keys);
}
catch
{
    Console.Error.WriteLine("HomeVault could not complete the operation. Check the documented configuration; no credentials were printed.");
    return 1;
}

static SensitiveStorageSession Unlock(string[] arguments)
{
    if (arguments.Length != 5 || Console.IsInputRedirected || Console.IsOutputRedirected) throw new InvalidOperationException();
    var locations = new[] { Path.GetDirectoryName(Path.GetFullPath(arguments[1]))!, arguments[2], arguments[3], arguments[4] }
        .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).ToArray();
    for (var i = 0; i < locations.Length; i++)
        for (var j = i + 1; j < locations.Length; j++)
            if (locations[i].Equals(locations[j], StringComparison.OrdinalIgnoreCase) ||
                locations[i].StartsWith(locations[j] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                locations[j].StartsWith(locations[i] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
    Console.WriteLine("Unlock with your separately stored random recovery key; never a human password.");
    var secret = EncryptionKeyJourney.ReadSecret();
    try { return SensitiveStorageSession.OpenWindows(arguments[3], arguments[4], secret); }
    finally { CryptographicOperations.ZeroMemory(secret); }
}
