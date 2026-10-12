using HomeVault.Application.Records;
using HomeVault.Application.Vaults;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class SqliteRecordNameTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;
    private Guid _owner, _vault, _asset;
    private SqliteRecordNameStore Store => new(_database);
    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVault-rename-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "records.db")); await _database.MigrateAsync();
        _owner = Guid.NewGuid(); _vault = Guid.NewGuid(); _asset = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(_vault, "Original", VaultType.Household, _owner).Vault!, default);
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(_asset, _vault, "Original").Asset!, _owner, default);
    }
    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);
    private Task<RenameOutcome> Rename(bool vault, Guid actor, string? name = "Replacement", string? expected = "Original") =>
        vault ? Store.RenameVaultAsync(_vault, actor, name, expected, default) : Store.RenameAssetAsync(_asset, actor, name, expected, default);

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3)]
    public async Task RoleAndArchiveChecksPrecedeNamesAndPreserveDeniedWrites(int role)
    {
        var actor = Guid.NewGuid();
        await using (var db = _database.CreateContext()) { db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = actor, Role = role }); await db.SaveChangesAsync(); }
        foreach (var vault in new[] { false, true })
        {
            var allowed = vault ? role <= 1 : role <= 2;
            Assert.That(await Rename(vault, actor), Is.EqualTo(allowed ? RenameOutcome.Succeeded : RenameOutcome.Forbidden));
            await using var read = _database.CreateContext();
            Assert.That(vault ? (await read.Vaults.SingleAsync()).Name : (await read.Assets.SingleAsync()).Name, Is.EqualTo(allowed ? "Replacement" : "Original"));
        }
        await new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default);
        foreach (var vault in new[] { false, true })
            Assert.That(await Rename(vault, actor, null, null), Is.EqualTo((vault ? role <= 1 : role <= 2) ? RenameOutcome.Archived : RenameOutcome.Forbidden));
    }

    [TestCase(false), TestCase(true)]
    public async Task ExpectedNameUsesOrdinalComparisonAndFailuresLeaveStoredNamesUnchanged(bool vault)
    {
        Assert.That(await Rename(vault, _owner, "new", "original"), Is.EqualTo(RenameOutcome.Conflict));
        Assert.That(await Rename(vault, _owner, " "), Is.EqualTo(RenameOutcome.InvalidName));
        Assert.That(await Rename(vault, _owner, "new", null), Is.EqualTo(RenameOutcome.InvalidExpectedName));
        Assert.That(await Rename(vault, Guid.NewGuid(), null, null), Is.EqualTo(RenameOutcome.Unavailable));
        Assert.That(await Rename(vault, _owner, "Original"), Is.EqualTo(RenameOutcome.Succeeded));
        Assert.That(await Rename(vault, _owner, " Étage/طبقه "), Is.EqualTo(RenameOutcome.Succeeded));
        Assert.That(await Rename(vault, _owner, "lost"), Is.EqualTo(RenameOutcome.Conflict));
        await using var db = _database.CreateContext();
        Assert.That(vault ? (await db.Vaults.SingleAsync()).Name : (await db.Assets.SingleAsync()).Name, Is.EqualTo(" Étage/طبقه "));
        Assert.That(await Store.RenameAssetAsync(Guid.NewGuid(), _owner, "new", "Original", default), Is.EqualTo(RenameOutcome.Unavailable));
        Assert.That(await Store.RenameVaultAsync(Guid.NewGuid(), _owner, "new", "Original", default), Is.EqualTo(RenameOutcome.Unavailable));
    }

    [Test]
    public async Task RenameReloadPreservesIdentityOwnershipAndAllRelatedRows()
    {
        var target = Guid.NewGuid(); var evidence = Guid.NewGuid(); var reminder = Guid.NewGuid(); var relationship = Guid.NewGuid();
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(target, _vault, "Target").Asset!, _owner, default);
        await using (var db = _database.CreateContext())
        {
            db.AssetAttributes.Add(new AssetAttributeRow { AssetId = _asset, Name = "Ordinary", Value = "Unchanged", Sensitivity = 0 });
            db.AssetEvidence.Add(new EvidenceRow { AssetId = _asset, Id = evidence, Label = "Note", Kind = 1, Content = "Fictional note" });
            db.Relationships.Add(new RelationshipRow { Id = relationship, VaultId = _vault, SourceAssetId = _asset, TargetAssetId = target, Kind = 0, Status = 0 });
            db.Reminders.Add(new ReminderRow { Id = reminder, VaultId = _vault, AssetId = _asset, Action = "Fictional action", DueAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks, Status = 0 });
            await db.SaveChangesAsync();
        }
        Assert.That(await Rename(false, _owner), Is.EqualTo(RenameOutcome.Succeeded));
        Assert.That(await Rename(true, _owner), Is.EqualTo(RenameOutcome.Succeeded));
        await using var read = _database.CreateContext();
        Assert.That((await read.Assets.SingleAsync(row => row.Id == _asset)).VaultId, Is.EqualTo(_vault));
        Assert.That((await read.Vaults.SingleAsync()).Type, Is.EqualTo((int)VaultType.Household));
        Assert.That((await read.Memberships.SingleAsync()).ActorId, Is.EqualTo(_owner));
        Assert.That((await read.AssetAttributes.SingleAsync()).Value, Is.EqualTo("Unchanged"));
        Assert.That((await read.AssetEvidence.SingleAsync()).Id, Is.EqualTo(evidence)); Assert.That((await read.AssetEvidence.SingleAsync()).Content, Is.EqualTo("Fictional note"));
        Assert.That((await read.Relationships.SingleAsync()).SourceAssetId, Is.EqualTo(_asset)); Assert.That((await read.Relationships.SingleAsync()).TargetAssetId, Is.EqualTo(target));
        Assert.That((await read.Reminders.SingleAsync()).Id, Is.EqualTo(reminder)); Assert.That((await read.Reminders.SingleAsync()).Action, Is.EqualTo("Fictional action"));
        Assert.That((await new SqliteAssetInspectionStore(_database).FindAsync(_asset, _owner, default))!.Name, Is.EqualTo("Replacement"));
    }

    [TestCase(false), TestCase(true)]
    public async Task ConcurrentNamesHaveOneWinnerAndOneConflict(bool vault)
    {
        var first = Task.Run(() => Rename(vault, _owner, "First")); var second = Task.Run(() => Rename(vault, _owner, "Second"));
        var outcomes = await Task.WhenAll(first, second);
        Assert.That(outcomes.Count(result => result == RenameOutcome.Succeeded), Is.EqualTo(1));
        Assert.That(outcomes.Count(result => result == RenameOutcome.Conflict), Is.EqualTo(1));
        await using var db = _database.CreateContext();
        Assert.That(vault ? (await db.Vaults.SingleAsync()).Name : (await db.Assets.SingleAsync()).Name,
            Is.EqualTo(outcomes[0] == RenameOutcome.Succeeded ? "First" : "Second"));
    }

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task RenameObservesCompetingCommittedArchiveOrPermissionChange(bool vault, bool archive)
    {
        await using var writer = _database.CreateContext(); await writer.Database.OpenConnectionAsync();
        await using var transaction = ((SqliteConnection)writer.Database.GetDbConnection()).BeginTransaction(false);
        await writer.Database.UseTransactionAsync(transaction);
        if (archive) (await writer.Vaults.SingleAsync()).Status = 1;
        else
        {
            writer.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = Guid.NewGuid(), Role = 0 });
            (await writer.Memberships.SingleAsync()).Role = 3;
        }
        await writer.SaveChangesAsync();
        var pending = Task.Run(() => Rename(vault, _owner)); await transaction.CommitAsync();
        Assert.That(await pending, Is.EqualTo(archive ? RenameOutcome.Archived : RenameOutcome.Forbidden));
        await using var read = _database.CreateContext();
        Assert.That(vault ? (await read.Vaults.SingleAsync()).Name : (await read.Assets.SingleAsync()).Name, Is.EqualTo("Original"));
    }

    [TestCase(false), TestCase(true)]
    public async Task StorageFailureRollsBackAndInvalidStoredMetadataFailsSafely(bool vault)
    {
        await using (var db = _database.CreateContext())
            await db.Database.ExecuteSqlRawAsync(vault
                ? "CREATE TRIGGER FailRename BEFORE UPDATE ON Vaults BEGIN SELECT RAISE(ABORT, 'fictional failure'); END;"
                : "CREATE TRIGGER FailRename BEFORE UPDATE ON Assets BEGIN SELECT RAISE(ABORT, 'fictional failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => Rename(vault, _owner));
        await using (var db = _database.CreateContext())
        {
            Assert.That(vault ? (await db.Vaults.SingleAsync()).Name : (await db.Assets.SingleAsync()).Name, Is.EqualTo("Original"));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER FailRename;");
            (await db.Vaults.SingleAsync()).Name = " "; await db.SaveChangesAsync();
        }
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Rename(vault, _owner));
        Assert.That(failure!.Message, Is.EqualTo("Invalid stored Vault state."));
    }
}
