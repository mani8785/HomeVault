using HomeVault.Application.Assets;
using HomeVault.Application.Vaults;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
public sealed class SqliteVaultArchiveTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;
    private Guid _owner;
    private Guid _vault;

    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVault-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "archive.db"));
        await _database.MigrateAsync();
        _owner = Guid.NewGuid();
        _vault = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(_vault, " Original ", VaultType.Household, _owner).Vault!, default);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task ChecksCurrentRoleEvenOnRepeatedArchiveAndPreservesRecords(int role)
    {
        var actor = Guid.NewGuid();
        var asset = Asset.Create(Guid.NewGuid(), _vault, "Stored").Asset!;
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(asset, _owner, default);
        await using (var db = _database.CreateContext())
        {
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = actor, Role = role });
            await db.SaveChangesAsync();
        }
        var adapter = new SqliteVaultArchiveStore(_database);
        var expected = role == 0 ? ArchiveVaultOutcome.Archived : ArchiveVaultOutcome.Forbidden;
        Assert.That(await adapter.ArchiveAsync(_vault, actor, default), Is.EqualTo(expected));
        await using (var db = _database.CreateContext())
            Assert.That((await db.Vaults.SingleAsync()).Status, Is.EqualTo(role == 0 ? 1 : 0));
        Assert.That(await adapter.ArchiveAsync(_vault, _owner, default), Is.EqualTo(ArchiveVaultOutcome.Archived));
        Assert.That(await adapter.ArchiveAsync(_vault, actor, default), Is.EqualTo(expected));
        await using var inspect = _database.CreateContext();
        Assert.That((await inspect.Vaults.SingleAsync()).Name, Is.EqualTo(" Original "));
        Assert.That(await inspect.Memberships.CountAsync(), Is.EqualTo(2));
        Assert.That((await inspect.Assets.SingleAsync()).Id, Is.EqualTo(asset.Id));
        Assert.That(await new SqliteAssetInspectionStore(_database).FindAsync(asset.Id, actor, default), Is.Not.Null);
        Assert.That(await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(Guid.NewGuid(), _vault, "Denied").Asset!, _owner, default), Is.EqualTo(AssetRegistrationOutcome.VaultArchived));
    }

    [Test]
    public async Task MissingNonmemberCancellationAndFailedWritePreserveState()
    {
        var adapter = new SqliteVaultArchiveStore(_database);
        Assert.That(await adapter.ArchiveAsync(_vault, Guid.NewGuid(), default), Is.EqualTo(ArchiveVaultOutcome.Unavailable));
        Assert.That(await adapter.ArchiveAsync(Guid.NewGuid(), _owner, default), Is.EqualTo(ArchiveVaultOutcome.Unavailable));
        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.ArchiveAsync(_vault, _owner, new CancellationToken(true)));
        await using (var db = _database.CreateContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailArchive BEFORE UPDATE ON Vaults BEGIN SELECT RAISE(ABORT, 'fictional failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => adapter.ArchiveAsync(_vault, _owner, default));
        await using var inspect = _database.CreateContext();
        Assert.That((await inspect.Vaults.SingleAsync()).Status, Is.Zero);
        Assert.That(await inspect.Memberships.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task InvalidStoredSnapshotFailsWithoutMutation()
    {
        await using (var db = _database.CreateContext())
        {
            (await db.Vaults.SingleAsync()).Name = " ";
            await db.SaveChangesAsync();
        }
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default));
        Assert.That(failure!.Message, Is.EqualTo("Invalid stored Vault state."));
        await using var inspect = _database.CreateContext();
        Assert.That((await inspect.Vaults.SingleAsync()).Status, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ArchiveObservesMembershipChangeCommittedByCompetingWriter(bool remove)
    {
        await using var writer = _database.CreateContext();
        await writer.Database.OpenConnectionAsync();
        await using var transaction = ((SqliteConnection)writer.Database.GetDbConnection()).BeginTransaction(false);
        await writer.Database.UseTransactionAsync(transaction);
        // A second Owner keeps the stored state valid after demotion/removal.
        writer.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = Guid.NewGuid(), Role = 0 });
        var owner = await writer.Memberships.SingleAsync();
        if (remove) writer.Memberships.Remove(owner); else owner.Role = 2;
        await writer.SaveChangesAsync();
        var pending = Task.Run(() => new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default));
        await transaction.CommitAsync();
        Assert.That(await pending, Is.EqualTo(remove ? ArchiveVaultOutcome.Unavailable : ArchiveVaultOutcome.Forbidden));
        await using var inspect = _database.CreateContext();
        Assert.That((await inspect.Vaults.SingleAsync()).Status, Is.Zero);
    }

    [Test]
    public async Task ConcurrentArchiveAndRegistrationHaveOnlySerializedOutcomes()
    {
        var archive = Task.Run(() => new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default));
        var registration = Task.Run(() => new SqliteAssetRegistrationStore(_database).RegisterAsync(
            Asset.Create(Guid.NewGuid(), _vault, "Concurrent").Asset!, _owner, default));
        await Task.WhenAll(archive, registration);
        Assert.That(await archive, Is.EqualTo(ArchiveVaultOutcome.Archived));
        Assert.That(await registration, Is.AnyOf(AssetRegistrationOutcome.Added, AssetRegistrationOutcome.VaultArchived));
        await using var inspect = _database.CreateContext();
        Assert.That((await inspect.Vaults.SingleAsync()).Status, Is.EqualTo(1));
        Assert.That(await inspect.Assets.CountAsync(), Is.EqualTo(await registration == AssetRegistrationOutcome.Added ? 1 : 0));
        Assert.That(await new SqliteAssetRegistrationStore(_database).RegisterAsync(
            Asset.Create(Guid.NewGuid(), _vault, "Late").Asset!, _owner, default), Is.EqualTo(AssetRegistrationOutcome.VaultArchived));
    }
}
