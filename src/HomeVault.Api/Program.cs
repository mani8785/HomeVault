using HomeVault.Api;
using HomeVault.Infrastructure.Identity;

try
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    if (args is ["private-directory", var directory])
    {
        PrivateOperatorFiles.InitializeDirectory(directory);
        Console.WriteLine("Private directory provisioned.");
        return 0;
    }
    if (args.Length < 3) throw new InvalidOperationException();
    using var lease = PrivateOperatorFiles.AcquireDatabase(args[1]);
    using var keys = WindowsSessionKeys.Open(args[2]);
    if (args[0] == "serve" && args.Length == 3)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration["AllowedHosts"] = "localhost";
        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(7443, endpoint => endpoint.UseHttps()));
        builder.Logging.ClearProviders();
        AuthenticationHost.Configure(builder.Services, args[1], keys);
        RecordsApi.Configure(builder.Services, args[1]);
        await using var app = builder.Build();
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().ValidateStorageAsync();
        AuthenticationHost.Map(app);
        RecordsApi.Map(app);
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
