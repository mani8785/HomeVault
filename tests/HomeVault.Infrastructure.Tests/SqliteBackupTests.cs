using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
public sealed class SqliteBackupTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;
    private Guid _owner;
    private Guid _assetId;

    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVaultTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "original.db"));
        await _database.MigrateAsync();
        _owner = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, _owner).Vault!;
        await new SqliteVaultRepository(_database).AddAsync(vault, default);
        _assetId = Guid.NewGuid();
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(_assetId, vault.Id, " Bicycle ").Asset!, _owner, default);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public async Task BackupAndRestorePreserveDataMembershipAndOriginal()
    {
        var backup = Path.Combine(_directory, "backup.db");
        var restore = Path.Combine(_directory, "restored.db");
        await _database.CreateVerifiedCopyAsync(backup);
        await new SqliteDatabase(backup).CreateVerifiedCopyAsync(restore);
        foreach (var database in new[] { _database, new SqliteDatabase(backup), new SqliteDatabase(restore) })
        {
            var reader = new SqliteAssetInspectionStore(database);
            Assert.That((await reader.FindAsync(_assetId, _owner, default))!.Name, Is.EqualTo(" Bicycle "));
            Assert.That(await reader.FindAsync(_assetId, Guid.NewGuid(), default), Is.Null);
        }
        var before = await File.ReadAllBytesAsync(restore);
        await Assert.ThrowsAsync<IOException>(() => _database.CreateVerifiedCopyAsync(restore));
        Assert.That(await File.ReadAllBytesAsync(restore), Is.EqualTo(before));
        Assert.That(Directory.GetFiles(_directory, "*.partial-*"), Is.Empty);
    }

    [Test]
    public async Task UnknownSchemaAndCancellationNeverPublishACopy()
    {
        var target = Path.Combine(_directory, "rejected.db");
        await Assert.ThrowsAsync<OperationCanceledException>(() => _database.CreateVerifiedCopyAsync(target, new CancellationToken(true)));
        Assert.That(File.Exists(target), Is.False);
        await using (var setup = _database.CreateContext())
            await setup.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('99999999999999_Future', '99.0.0')");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _database.CreateVerifiedCopyAsync(target));
        Assert.That(File.Exists(target), Is.False);
        Assert.That(Directory.GetFiles(_directory, "*.partial-*"), Is.Empty);
    }

    [Test]
    public async Task RecoveryRestoresPriorSchemaWithoutDestructiveDownMigration()
    {
        var old = new SqliteDatabase(Path.Combine(_directory, "old.db"));
        var id = Guid.NewGuid();
        await using (var context = old.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(context.Database.GetMigrations().First());
            context.Vaults.Add(new VaultRow { Id = id, Name = "Prior version", Type = 0, Status = 0 });
            context.Memberships.Add(new MembershipRow { VaultId = id, ActorId = _owner, Role = 0 });
            await context.SaveChangesAsync();
        }
        var backup = Path.Combine(_directory, "before-upgrade.db");
        await old.CreateVerifiedCopyAsync(backup);
        await old.MigrateAsync();
        var recoveredPath = Path.Combine(_directory, "recovered.db");
        await new SqliteDatabase(backup).CreateVerifiedCopyAsync(recoveredPath);
        var recovered = new SqliteDatabase(recoveredPath);
        await using (var inspect = recovered.CreateContext())
        {
            Assert.That((await inspect.Database.GetAppliedMigrationsAsync()).Count(), Is.EqualTo(1));
            Assert.That((await inspect.Vaults.SingleAsync()).Name, Is.EqualTo("Prior version"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => SqliteDatabase.ValidateHistoryAsync(inspect, true, default));
        }
        await recovered.MigrateAsync();
        await using var upgraded = recovered.CreateContext();
        Assert.That((await upgraded.Vaults.SingleAsync()).Id, Is.EqualTo(id));
        Assert.That(await upgraded.Assets.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task CorruptInputIsNotPublishedOrOverwritten()
    {
        var corrupt = Path.Combine(_directory, "corrupt.db");
        await File.WriteAllTextAsync(corrupt, "Fictional invalid database");
        var target = Path.Combine(_directory, "invalid-restore.db");
        await Assert.CatchAsync<Exception>(() => new SqliteDatabase(corrupt).CreateVerifiedCopyAsync(target));
        Assert.That(File.Exists(target), Is.False);
        Assert.That(await File.ReadAllTextAsync(corrupt), Is.EqualTo("Fictional invalid database"));
        Assert.That(Directory.GetFiles(_directory, "*.partial-*"), Is.Empty);
    }
}
