using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
public sealed class SqliteInspectionTests
{
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, false)]
    [TestCase(0, true)]
    [TestCase(3, true)]
    public async Task ReopenedReadChecksCurrentMembershipAndPreservesMetadata(int role, bool archived)
    {
        var directory = Path.Combine(Path.GetTempPath(), "HomeVaultTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var database = new SqliteDatabase(Path.Combine(directory, "read.db"));
            await database.MigrateAsync();
            var owner = Guid.NewGuid();
            var reader = Guid.NewGuid();
            var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Household, owner).Vault!;
            await new SqliteVaultRepository(database).AddAsync(vault, default);
            var asset = Asset.Create(Guid.NewGuid(), vault.Id, " Original ").Asset!;
            await new SqliteAssetRegistrationStore(database).RegisterAsync(asset, owner, default);
            await using (var setup = database.CreateContext())
            {
                setup.Memberships.Add(new MembershipRow { VaultId = vault.Id, ActorId = reader, Role = role });
                (await setup.Vaults.SingleAsync()).Status = archived ? 1 : 0;
                await setup.SaveChangesAsync();
            }
            var adapter = new SqliteAssetInspectionStore(new SqliteDatabase(Path.Combine(directory, "read.db")));
            var view = await adapter.FindAsync(asset.Id, reader, default);
            Assert.That(view!.Name, Is.EqualTo(" Original "));
            Assert.That(view.Id, Is.EqualTo(asset.Id));
            Assert.That(view.VaultId, Is.EqualTo(vault.Id));
            Assert.That(view.ToString(), Is.EqualTo("InspectedAsset"));
            Assert.That(await adapter.FindAsync(asset.Id, Guid.NewGuid(), default), Is.Null);
            Assert.That(await adapter.FindAsync(Guid.NewGuid(), reader, default), Is.Null);
            await using (var setup = database.CreateContext())
            {
                setup.Memberships.Remove(await setup.Memberships.SingleAsync(row => row.ActorId == reader));
                await setup.SaveChangesAsync();
            }
            Assert.That(await adapter.FindAsync(asset.Id, reader, default), Is.Null);
            Assert.That(view.Name, Is.EqualTo(" Original "));
        }
        finally { Directory.Delete(directory, true); }
    }
}
