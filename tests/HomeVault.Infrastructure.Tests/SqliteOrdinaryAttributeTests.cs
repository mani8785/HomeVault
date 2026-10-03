using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Persistence;
using HomeVault.Infrastructure.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class SqliteOrdinaryAttributeTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;
    private SqliteOrdinaryAttributeStore _store = null!;
    private Guid _owner, _vault, _asset;

    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVault-attributes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "attributes.db"));
        await _database.MigrateAsync();
        _store = new SqliteOrdinaryAttributeStore(_database);
        _owner = Guid.NewGuid(); _vault = Guid.NewGuid(); _asset = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(_vault, "Vault", VaultType.Personal, _owner).Vault!, default);
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(_asset, _vault, "Asset").Asset!, _owner, default);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    private Task<AttributeOutcome> Add(string name = "Étage/طبقه", string value = " first ", AttributeSensitivity sensitivity = AttributeSensitivity.Ordinary) =>
        _store.AddAsync(_asset, _owner, name, value, sensitivity, default);

    [Test]
    public async Task RoundTripUsesDomainEqualityPreservesOtherRowsAndSnapshots()
    {
        Assert.That(await Add(), Is.EqualTo(AttributeOutcome.Succeeded));
        Assert.That(await Add("unrelated", "keep"), Is.EqualTo(AttributeOutcome.Succeeded));
        var before = await _store.ListAsync(_asset, _owner, default);
        Assert.That(await Add(" étage/طبقه "), Is.EqualTo(AttributeOutcome.DuplicateName));
        Assert.That(await _store.ChangeAsync(_asset, _owner, " étage/طبقه ", " replacement ", default), Is.EqualTo(AttributeOutcome.Succeeded));
        var after = await new SqliteOrdinaryAttributeStore(_database).ListAsync(_asset, _owner, default);
        Assert.That(after!.Single(entry => entry.Name == "Étage/طبقه").Value, Is.EqualTo(" replacement "));
        Assert.That(before!.Single(entry => entry.Name == "Étage/طبقه").Value, Is.EqualTo(" first "));
        Assert.That(await _store.ChangeAsync(_asset, _owner, "Étage/طبقه", " replacement ", default), Is.EqualTo(AttributeOutcome.Succeeded));
        Assert.That(await _store.RemoveAsync(_asset, _owner, "ÉTAGE/طبقه", default), Is.EqualTo(AttributeOutcome.Succeeded));
        Assert.That((await _store.ListAsync(_asset, _owner, default))!.Single().Value, Is.EqualTo("keep"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task AllRolesAndArchivedReadsAndWrites(int role)
    {
        await Add();
        var actor = Guid.NewGuid();
        await using (var db = _database.CreateContext())
        {
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = actor, Role = role });
            await db.SaveChangesAsync();
        }
        var expected = role == 3 ? AttributeOutcome.Forbidden : AttributeOutcome.Succeeded;
        Assert.That(await _store.AddAsync(_asset, actor, "New", "value", AttributeSensitivity.Ordinary, default), Is.EqualTo(expected));
        Assert.That(await _store.ChangeAsync(_asset, actor, "Étage/طبقه", "changed", default), Is.EqualTo(expected));
        Assert.That(await _store.RemoveAsync(_asset, actor, "Étage/طبقه", default), Is.EqualTo(expected));
        await new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default);
        var archived = role == 3 ? AttributeOutcome.Forbidden : AttributeOutcome.Archived;
        Assert.That(await _store.AddAsync(_asset, actor, "Other", "value", AttributeSensitivity.Ordinary, default), Is.EqualTo(archived));
        Assert.That(await _store.ChangeAsync(_asset, actor, "New", "changed", default), Is.EqualTo(archived));
        Assert.That(await _store.RemoveAsync(_asset, actor, "New", default), Is.EqualTo(archived));
        Assert.That(await _store.ListAsync(_asset, actor, default), Is.Not.Null);
    }

    [Test]
    public async Task CrossVaultAndMissingRequestsAreIndistinguishableAndFailuresPreserveRows()
    {
        await Add();
        var outsider = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(Guid.NewGuid(), "Other", VaultType.Personal, outsider).Vault!, default);
        foreach (var id in new[] { _asset, Guid.NewGuid() })
        {
            Assert.That(await _store.ListAsync(id, outsider, default), Is.Null);
            Assert.That(await _store.AddAsync(id, outsider, " ", " ", AttributeSensitivity.Sensitive, default), Is.EqualTo(AttributeOutcome.Unavailable));
            Assert.That(await _store.ChangeAsync(id, outsider, "Étage/طبقه", "overwrite", default), Is.EqualTo(AttributeOutcome.Unavailable));
            Assert.That(await _store.RemoveAsync(id, outsider, "Étage/طبقه", default), Is.EqualTo(AttributeOutcome.Unavailable));
        }
        Assert.That(await Add(" ", " ", AttributeSensitivity.Sensitive), Is.EqualTo(AttributeOutcome.InvalidName));
        Assert.That(await Add("new", " ", AttributeSensitivity.Sensitive), Is.EqualTo(AttributeOutcome.InvalidValue));
        Assert.That(await Add("new", "Private sentinel", AttributeSensitivity.Sensitive), Is.EqualTo(AttributeOutcome.UnsupportedSensitivity));
        Assert.That(await Add("new", "value", (AttributeSensitivity)99), Is.EqualTo(AttributeOutcome.UnsupportedSensitivity));
        Assert.That(await _store.ChangeAsync(_asset, _owner, "absent", "new", default), Is.EqualTo(AttributeOutcome.Unavailable));
        Assert.That(await _store.RemoveAsync(_asset, _owner, "absent", default), Is.EqualTo(AttributeOutcome.Unavailable));
        await Assert.ThrowsAsync<OperationCanceledException>(() => _store.RemoveAsync(_asset, _owner, "Étage/طبقه", new CancellationToken(true)));
        Assert.That((await _store.ListAsync(_asset, _owner, default))!.Single().Value, Is.EqualTo(" first "));
    }

    [TestCase("add")]
    [TestCase("change")]
    [TestCase("remove")]
    public async Task DatabaseFailuresRollBackOnlyAttemptedMutation(string operation)
    {
        await Add();
        await using (var db = _database.CreateContext())
        {
            var sql = operation switch
            {
                "add" => "CREATE TRIGGER RejectAttribute BEFORE INSERT ON AssetAttributes BEGIN SELECT RAISE(ABORT, 'fictional failure'); END;",
                "change" => "CREATE TRIGGER RejectAttribute BEFORE UPDATE ON AssetAttributes BEGIN SELECT RAISE(ABORT, 'fictional failure'); END;",
                _ => "CREATE TRIGGER RejectAttribute BEFORE DELETE ON AssetAttributes BEGIN SELECT RAISE(ABORT, 'fictional failure'); END;"
            };
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        await Assert.ThrowsAsync<Exception>(async () =>
        {
            try
            {
                if (operation == "add") await Add("new");
                else if (operation == "change") await _store.ChangeAsync(_asset, _owner, "Étage/طبقه", "changed", default);
                else await _store.RemoveAsync(_asset, _owner, "Étage/طبقه", default);
            }
            catch (Exception exception) when (exception is DbUpdateException or SqliteException) { throw new Exception("Expected write failure."); }
        });
        Assert.That((await _store.ListAsync(_asset, _owner, default))!.Single().Value, Is.EqualTo(" first "));
    }

    [Test]
    public async Task SensitiveConstraintAndCorruptClassificationFailClosed()
    {
        await Add();
        await using (var db = _database.CreateContext())
        {
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE AssetAttributes SET Sensitivity = 1"));
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints=ON");
            await db.Database.ExecuteSqlRawAsync("UPDATE AssetAttributes SET Sensitivity = 1, Value = 'Private sentinel'");
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ListAsync(_asset, _owner, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Add("new"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ChangeAsync(_asset, _owner, "Étage/طبقه", "overwrite", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.RemoveAsync(_asset, _owner, "Étage/طبقه", default));
        await using var inspect = _database.CreateContext();
        Assert.That((await inspect.AssetAttributes.SingleAsync()).Value, Is.EqualTo("Private sentinel"));
        Assert.That((await inspect.AssetAttributes.SingleAsync()).Sensitivity, Is.EqualTo(1));
    }

    [Test]
    public async Task ConcurrentCaseVariantsProduceOneRow()
    {
        var results = await Task.WhenAll(Task.Run(() => Add("Étage")), Task.Run(() => Add("étage")));
        Assert.That(results, Is.EquivalentTo(new[] { AttributeOutcome.Succeeded, AttributeOutcome.DuplicateName }));
        Assert.That((await _store.ListAsync(_asset, _owner, default))!.Count, Is.EqualTo(1));
    }

    [TestCase("archive")]
    [TestCase("demote")]
    [TestCase("remove")]
    public async Task CompetingPermissionChangesAreObservedBeforeMutation(string change)
    {
        await Add();
        await using var writer = _database.CreateContext();
        await writer.Database.OpenConnectionAsync();
        await using var transaction = ((SqliteConnection)writer.Database.GetDbConnection()).BeginTransaction(false);
        await writer.Database.UseTransactionAsync(transaction);
        writer.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = Guid.NewGuid(), Role = 0 });
        if (change == "archive") (await writer.Vaults.SingleAsync()).Status = 1;
        else if (change == "demote") (await writer.Memberships.SingleAsync()).Role = 3;
        else writer.Memberships.Remove(await writer.Memberships.SingleAsync());
        await writer.SaveChangesAsync();
        var pending = Task.Run(() => _store.ChangeAsync(_asset, _owner, "Étage/طبقه", "denied", default));
        await transaction.CommitAsync();
        Assert.That(await pending, Is.EqualTo(change == "archive" ? AttributeOutcome.Archived : change == "demote" ? AttributeOutcome.Forbidden : AttributeOutcome.Unavailable));
        await using var inspect = _database.CreateContext();
        Assert.That((await inspect.AssetAttributes.SingleAsync()).Value, Is.EqualTo(" first "));
    }

    [Test]
    public async Task UpgradeAndBackupRestorePreservePriorDataAndNewAttributes()
    {
        var prior = new SqliteDatabase(Path.Combine(_directory, "prior.db"));
        await using (var db = prior.CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261002062200_AddAccountCredentials");
            db.Vaults.Add(new VaultRow { Id = _vault, Name = "Preserved", Type = 0, Status = 0 });
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = _owner, Role = 0 });
            db.Assets.Add(new AssetRow { Id = _asset, VaultId = _vault, Name = "Preserved Asset" });
            db.Users.Add(new HomeVaultUser { Id = _owner, UserName = "owner@example.invalid", NormalizedUserName = "OWNER@EXAMPLE.INVALID", SecurityStamp = "fictional-stamp" });
            db.Set<AccountCredentialRow>().Add(new AccountCredentialRow { Hash = "fictional-hash", Login = "OWNER@EXAMPLE.INVALID", Purpose = "invitation", Expires = 123456, Consumed = true });
            await db.SaveChangesAsync();
        }
        await prior.MigrateAsync();
        var store = new SqliteOrdinaryAttributeStore(prior);
        Assert.That(await store.ListAsync(_asset, _owner, default), Is.Empty);
        await store.AddAsync(_asset, _owner, "Label", " stored ", AttributeSensitivity.Ordinary, default);
        var backup = Path.Combine(_directory, "backup.db");
        var restored = Path.Combine(_directory, "restored.db");
        await prior.CreateVerifiedCopyAsync(backup);
        await new SqliteDatabase(backup).CreateVerifiedCopyAsync(restored);
        foreach (var database in new[] { prior, new SqliteDatabase(backup), new SqliteDatabase(restored) })
        {
            Assert.That((await new SqliteOrdinaryAttributeStore(database).ListAsync(_asset, _owner, default))!.Single().Value, Is.EqualTo(" stored "));
            Assert.That((await new SqliteAssetInspectionStore(database).FindAsync(_asset, _owner, default))!.Name, Is.EqualTo("Preserved Asset"));
            await using var inspect = database.CreateContext();
            Assert.That((await inspect.Users.SingleAsync()).SecurityStamp, Is.EqualTo("fictional-stamp"));
            var credential = await inspect.Set<AccountCredentialRow>().SingleAsync();
            Assert.That(credential.Hash, Is.EqualTo("fictional-hash"));
            Assert.That(credential.Consumed, Is.True);
            Assert.That(credential.Expires, Is.EqualTo(123456));
        }
    }

    [Test]
    public async Task ExternalLogicalDuplicatesAreRejectedWithoutMutation()
    {
        await Add("Étage");
        await using (var db = _database.CreateContext())
        {
            db.AssetAttributes.Add(new AssetAttributeRow { AssetId = _asset, Name = "étage", Value = "Private sentinel", Sensitivity = 0 });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ListAsync(_asset, _owner, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.RemoveAsync(_asset, _owner, "étage", default));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.AssetAttributes.CountAsync(), Is.EqualTo(2));
    }
}
