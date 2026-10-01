using HomeVault.Application.Assets;
using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Persistence;

namespace HomeVault.Playground;

internal static class DurableJourney
{
    private static readonly Guid ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid VaultId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AssetId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    internal static async Task<int> RunAsync(string[] args)
    {
        var copy = args.Length == 4 && args[1] is "backup" or "restore";
        if (!copy && (args.Length != 3 || args[1] is not ("migrate" or "create" or "read")))
        {
            Console.Error.WriteLine("Usage: storage migrate|create|read <absolute-path>, or storage backup|restore <source> <new-destination>");
            return 2;
        }
        try
        {
            var database = new SqliteDatabase(args[2]);
            var actor = new ExampleCurrentActor(ActorId);
            if (copy)
            {
                await database.CreateVerifiedCopyAsync(args[3]);
                Console.WriteLine("Verified copy created; source preserved. Verify expected records before switching paths.");
            }
            else if (args[1] == "migrate")
            {
                await database.MigrateAsync();
                Console.WriteLine("Explicit schema migration completed.");
            }
            else if (args[1] == "create")
            {
                var vault = await new CreateVaultUseCase(actor, new SqliteVaultRepository(database))
                    .ExecuteAsync(new CreateVaultRequest(VaultId, "Fictional household", VaultType.Household));
                if (!vault.IsSuccess) throw new InvalidOperationException("Expected a fresh demonstration database.");
                var asset = await new RegisterAssetUseCase(actor, new SqliteAssetRegistrationStore(database))
                    .ExecuteAsync(new RegisterAssetRequest(AssetId, VaultId, " Fictional bicycle "));
                if (!asset.IsSuccess) throw new InvalidOperationException("Registration failed.");
                Console.WriteLine("Fictional Vault and Asset saved. Run storage read in a new process.");
            }
            else
            {
                var inspection = new InspectAssetUseCase(actor, new SqliteAssetInspectionStore(database));
                var asset = await inspection.ExecuteAsync(AssetId);
                if (asset is null || asset.Id != AssetId || asset.VaultId != VaultId || asset.Name != " Fictional bicycle ")
                    throw new InvalidOperationException("Reload verification failed.");
                var denied = await new InspectAssetUseCase(new ExampleCurrentActor(Guid.NewGuid()), new SqliteAssetInspectionStore(database)).ExecuteAsync(AssetId);
                if (denied is not null) throw new InvalidOperationException("Isolation verification failed.");
                Console.WriteLine("Durable reload and nonmember isolation verified.");
            }
            return 0;
        }
        catch (Exception)
        {
            // Paths, connection details, and private values must not appear in diagnostics.
            Console.Error.WriteLine("Storage operation failed. Check schema, path, permissions, and demonstration state.");
            return 1;
        }
    }
}
