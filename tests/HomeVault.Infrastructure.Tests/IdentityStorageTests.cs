using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
public sealed class IdentityStorageTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVaultTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "identity.db"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public async Task IdentityUpgradePreservesAssetsAndDoesNotClaimFictionalMemberships()
    {
        var vaultId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        await using (var old = _database.CreateContext())
        {
            await old.GetService<IMigrator>().MigrateAsync("20261001112957_AddAssets");
            old.Vaults.Add(new VaultRow { Id = vaultId, Name = " Fictional household ", Type = 1 });
            old.Memberships.Add(new MembershipRow { VaultId = vaultId, ActorId = actorId, Role = 0 });
            old.Assets.Add(new AssetRow { Id = assetId, VaultId = vaultId, Name = " Fictional bicycle " });
            await old.SaveChangesAsync();
        }
        await _database.MigrateAsync();
        await using var current = _database.CreateContext();
        Assert.That(await current.Users.CountAsync(), Is.Zero);
        Assert.That((await current.Memberships.SingleAsync()).ActorId, Is.EqualTo(actorId));
        Assert.That((await current.Vaults.SingleAsync()).Name, Is.EqualTo(" Fictional household "));
        Assert.That((await current.Assets.SingleAsync()).Id, Is.EqualTo(assetId));
        Assert.That((await current.Assets.SingleAsync()).Name, Is.EqualTo(" Fictional bicycle "));
        Assert.That(current.Database.HasPendingModelChanges(), Is.False);
    }

    [Test]
    public async Task IdentityManagerHashesPasswordsAndAccountsSurviveBackupAndRestore()
    {
        await _database.MigrateAsync();
        var user = new HomeVaultUser { UserName = "fictional@example.invalid", Email = "fictional@example.invalid" };
        const string password = "Fictional-only Password 123!";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _database.CreateContext());
        services.AddIdentityCore<HomeVaultUser>().AddEntityFrameworkStores<HomeVaultDbContext>();
        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<HomeVaultUser>>();
            Assert.That((await manager.CreateAsync(user, password)).Succeeded, Is.True);
            Assert.That(await manager.CheckPasswordAsync(user, password), Is.True);
            Assert.That(await manager.CheckPasswordAsync(user, "Wrong fictional password"), Is.False);
            Assert.That(user.PasswordHash, Is.Not.Null.And.Not.EqualTo(password));
            Assert.That(user.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(user.SecurityStamp, Is.Not.Null.And.Not.Empty);
            Assert.That(user.ToString(), Is.EqualTo(nameof(HomeVaultUser)));
            var duplicate = new HomeVaultUser { UserName = "FICTIONAL@example.invalid" };
            Assert.That((await manager.CreateAsync(duplicate, password)).Succeeded, Is.False);
        }
        var backup = Path.Combine(_directory, "backup.db");
        var restored = Path.Combine(_directory, "restored.db");
        await _database.CreateVerifiedCopyAsync(backup);
        await new SqliteDatabase(backup).CreateVerifiedCopyAsync(restored);
        await using var reopened = new SqliteDatabase(restored).CreateContext();
        var saved = await reopened.Users.SingleAsync();
        Assert.That(saved.Id, Is.EqualTo(user.Id));
        Assert.That(saved.IsEnabled, Is.True);
        Assert.That(new PasswordHasher<HomeVaultUser>().VerifyHashedPassword(saved, saved.PasswordHash!, password),
            Is.Not.EqualTo(PasswordVerificationResult.Failed));
        Assert.That(await reopened.Memberships.CountAsync(), Is.Zero);
        await SqliteDatabase.ValidateHistoryAsync(reopened, true, default);
    }

    [Test]
    public async Task DatabaseRejectsEmptyAccountIdentity()
    {
        await _database.MigrateAsync();
        await using var context = _database.CreateContext();
        context.Users.Add(new HomeVaultUser { Id = Guid.Empty, UserName = "fictional" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
