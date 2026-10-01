using HomeVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
public sealed class SqliteSchemaTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVaultTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "schema.db"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public async Task UpgradesPopulatedVaultSchemaAndPreservesData()
    {
        var id = Guid.NewGuid();
        await using (var context = _database.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(context.Database.GetMigrations().First());
            context.Vaults.Add(new VaultRow { Id = id, Name = " Original ", Type = 1, Status = 0 });
            context.Memberships.Add(new MembershipRow { VaultId = id, ActorId = Guid.NewGuid(), Role = 0 });
            await context.SaveChangesAsync();
        }
        await _database.MigrateAsync();
        await using var reopened = _database.CreateContext();
        Assert.That((await reopened.Vaults.SingleAsync()).Name, Is.EqualTo(" Original "));
        Assert.That(await reopened.Memberships.CountAsync(), Is.EqualTo(1));
        reopened.Assets.Add(new AssetRow { Id = Guid.NewGuid(), VaultId = id, Name = "Bicycle" });
        await reopened.SaveChangesAsync();
        await SqliteDatabase.ValidateHistoryAsync(reopened, true, default);
        Assert.That(await reopened.Assets.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task FreshMigrationIsRepeatableAndModelMatchesSnapshot()
    {
        await _database.MigrateAsync();
        await _database.MigrateAsync();
        await using var context = _database.CreateContext();
        Assert.That(context.Database.HasPendingModelChanges(), Is.False);
        Assert.That(await context.Database.GetAppliedMigrationsAsync(), Is.EqualTo(context.Database.GetMigrations()));
        Assert.That(await context.Assets.CountAsync(), Is.Zero);
    }

    [TestCase("INSERT INTO Vaults (Id, Name, Type, Status) VALUES ('11111111-1111-1111-1111-111111111111', 'Example', 9, 0)")]
    [TestCase("INSERT INTO Vaults (Id, Name, Type, Status) VALUES ('11111111-1111-1111-1111-111111111111', 'Example', 0, 9)")]
    [TestCase("INSERT INTO Assets (Id, VaultId, Name) VALUES ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 'Example')")]
    [TestCase("INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 0)")]
    public async Task DatabaseRejectsInvalidStateAndOrphans(string sql)
    {
        await _database.MigrateAsync();
        await using var context = _database.CreateContext();
        var failure = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(sql));
        Assert.That(failure!.SqliteErrorCode, Is.EqualTo(19));
    }

    [Test]
    public async Task RejectsUnknownFutureSchemaBeforeMigration()
    {
        await _database.MigrateAsync();
        await using (var context = _database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('99999999999999_Future', '99.0.0')");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _database.MigrateAsync());
    }
}
