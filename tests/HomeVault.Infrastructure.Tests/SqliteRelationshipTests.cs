using HomeVault.Application.Relationships;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Relationships;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class SqliteRelationshipTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;
    private SqliteRelationshipStore _store = null!;
    private Guid _vault, _owner, _source, _target, _id;
    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVault-relationships-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "relationships.db"));
        await _database.MigrateAsync();
        _store = new SqliteRelationshipStore(_database);
        _vault = Guid.NewGuid(); _owner = Guid.NewGuid(); _source = Guid.NewGuid(); _target = Guid.NewGuid(); _id = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(_vault, "Vault", VaultType.Personal, _owner).Vault!, default);
        foreach (var id in new[] { _source, _target })
            await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(id, _vault, "Asset").Asset!, _owner, default);
    }
    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);
    private Task<RelationshipOutcome> Create(Guid? id = null) => _store.CreateAsync(_vault, _owner, id ?? _id, _source, _target, RelationshipKind.Covers, default);

    [Test]
    public async Task DuplicatesReverseDirectionRemovalAndRecreationRetainEndpointsAndComponents()
    {
        await new SqliteOrdinaryAttributeStore(_database).AddAsync(_source, _owner, "Material", "keep", AttributeSensitivity.Ordinary, default);
        var evidence = Guid.NewGuid();
        await new SqliteEvidenceStore(_database).AddAsync(_target, _owner, evidence, "Label", EvidenceKind.Note, "keep", default);
        Assert.That(await Create(), Is.EqualTo(RelationshipOutcome.Succeeded));
        var before = await _store.FindAsync(_vault, _owner, _id, default);
        Assert.That(before!.SourceAssetId, Is.EqualTo(_source));
        Assert.That(before.TargetAssetId, Is.EqualTo(_target));
        Assert.That(await Create(), Is.EqualTo(RelationshipOutcome.IdentityConflict));
        Assert.That(await Create(Guid.NewGuid()), Is.EqualTo(RelationshipOutcome.DuplicateRelationship));
        Assert.That(await _store.CreateAsync(_vault, _owner, Guid.NewGuid(), _target, _source, RelationshipKind.Covers, default), Is.EqualTo(RelationshipOutcome.Succeeded));
        Assert.That(await _store.RemoveAsync(_vault, _owner, _id, default), Is.EqualTo(RelationshipOutcome.Succeeded));
        Assert.That(await _store.RemoveAsync(_vault, _owner, _id, default), Is.EqualTo(RelationshipOutcome.Succeeded));
        Assert.That(before.Status, Is.EqualTo(RelationshipStatus.Active));
        Assert.That((await _store.FindAsync(_vault, _owner, _id, default))!.Status, Is.EqualTo(RelationshipStatus.Removed));
        Assert.That(await Create(), Is.EqualTo(RelationshipOutcome.IdentityConflict));
        Assert.That(await Create(Guid.NewGuid()), Is.EqualTo(RelationshipOutcome.Succeeded));
        Assert.That((await new SqliteOrdinaryAttributeStore(_database).ListAsync(_source, _owner, default))!.Single().Value, Is.EqualTo("keep"));
        Assert.That((await new SqliteEvidenceStore(_database).ReadContentAsync(_target, _owner, evidence, default))!.ReadContent(), Is.EqualTo("keep"));
        await using var db = _database.CreateContext();
        Assert.That(await db.Assets.CountAsync(), Is.EqualTo(2));
        Assert.That(await db.Relationships.CountAsync(), Is.EqualTo(3));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task AllRolesAndArchiveApplyToRepeatedRemoval(int role)
    {
        var actor = Guid.NewGuid();
        await using (var db = _database.CreateContext())
        {
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = actor, Role = role });
            await db.SaveChangesAsync();
        }
        var expected = role == 3 ? RelationshipOutcome.Forbidden : RelationshipOutcome.Succeeded;
        Assert.That(await _store.CreateAsync(_vault, actor, _id, _source, _target, RelationshipKind.Covers, default), Is.EqualTo(expected));
        if (role == 3) await Create();
        Assert.That(await _store.FindAsync(_vault, actor, _id, default), Is.Not.Null);
        Assert.That(await _store.RemoveAsync(_vault, actor, _id, default), Is.EqualTo(expected));
        await _store.RemoveAsync(_vault, _owner, _id, default);
        await new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default);
        var archived = role == 3 ? RelationshipOutcome.Forbidden : RelationshipOutcome.Archived;
        Assert.That(await _store.RemoveAsync(_vault, actor, _id, default), Is.EqualTo(archived));
        Assert.That(await _store.CreateAsync(_vault, actor, Guid.NewGuid(), _source, _target, RelationshipKind.Covers, default), Is.EqualTo(archived));
        Assert.That((await _store.FindAsync(_vault, actor, _id, default))!.Status, Is.EqualTo(RelationshipStatus.Removed));
    }

    [Test]
    public async Task SharedMembershipDoesNotAuthorizeCrossVaultEndpointsOrExposeForeignRootIds()
    {
        await Create();
        var otherVault = Guid.NewGuid(); var otherAsset = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(otherVault, "Other", VaultType.Personal, _owner).Vault!, default);
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(otherAsset, otherVault, "Other").Asset!, _owner, default);
        foreach (var endpoint in new[] { otherAsset, Guid.NewGuid() })
        {
            Assert.That(await _store.CreateAsync(_vault, _owner, Guid.NewGuid(), _source, endpoint, (RelationshipKind)99, default), Is.EqualTo(RelationshipOutcome.Unavailable));
            Assert.That(await _store.CreateAsync(_vault, _owner, Guid.NewGuid(), endpoint, _target, RelationshipKind.Covers, default), Is.EqualTo(RelationshipOutcome.Unavailable));
        }
        Assert.That(await _store.FindAsync(otherVault, _owner, _id, default), Is.Null);
        Assert.That(await _store.RemoveAsync(otherVault, _owner, _id, default), Is.EqualTo(RelationshipOutcome.Unavailable));
        var another = Guid.NewGuid();
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(another, otherVault, "Other").Asset!, _owner, default);
        Assert.That(await _store.CreateAsync(otherVault, _owner, _id, otherAsset, another, RelationshipKind.Covers, default), Is.EqualTo(RelationshipOutcome.Unavailable));
        var outsider = Guid.NewGuid();
        Assert.That(await _store.FindAsync(_vault, outsider, _id, default), Is.Null);
        Assert.That(await _store.RemoveAsync(_vault, outsider, _id, default), Is.EqualTo(RelationshipOutcome.Unavailable));
        Assert.That(await _store.CreateAsync(_vault, outsider, Guid.NewGuid(), _source, _target, RelationshipKind.Covers, default), Is.EqualTo(RelationshipOutcome.Unavailable));
    }

    [Test]
    public async Task KindAndSelfValidationFollowEndpointChecksAndCancellationPreservesState()
    {
        Assert.That(await _store.CreateAsync(_vault, _owner, _id, _source, _source, (RelationshipKind)99, default), Is.EqualTo(RelationshipOutcome.InvalidKind));
        Assert.That(await _store.CreateAsync(_vault, _owner, _id, _source, _source, RelationshipKind.Covers, default), Is.EqualTo(RelationshipOutcome.SelfReference));
        await Create();
        await Assert.ThrowsAsync<OperationCanceledException>(() => _store.RemoveAsync(_vault, _owner, _id, new CancellationToken(true)));
        Assert.That((await _store.FindAsync(_vault, _owner, _id, default))!.Status, Is.EqualTo(RelationshipStatus.Active));
    }

    [Test]
    public async Task ConcurrentDuplicateAndRemovalRecreationAreSerialized()
    {
        var duplicate = Guid.NewGuid();
        var results = await Task.WhenAll(Task.Run(() => Create()), Task.Run(() => Create(duplicate)));
        Assert.That(results, Is.EquivalentTo(new[] { RelationshipOutcome.Succeeded, RelationshipOutcome.DuplicateRelationship }));
        var existing = results[0] == RelationshipOutcome.Succeeded ? _id : duplicate;
        var newId = Guid.NewGuid();
        var remove = Task.Run(() => _store.RemoveAsync(_vault, _owner, existing, default));
        var recreate = Task.Run(() => Create(newId));
        await Task.WhenAll(remove, recreate);
        Assert.That(await remove, Is.EqualTo(RelationshipOutcome.Succeeded));
        Assert.That(await recreate, Is.AnyOf(RelationshipOutcome.Succeeded, RelationshipOutcome.DuplicateRelationship));
        if (await recreate == RelationshipOutcome.DuplicateRelationship) Assert.That(await Create(newId), Is.EqualTo(RelationshipOutcome.Succeeded));
        await using var db = _database.CreateContext();
        Assert.That(await db.Relationships.CountAsync(row => row.Status == 0), Is.EqualTo(1));
        Assert.That(await db.Relationships.CountAsync(row => row.Status == 1), Is.EqualTo(1));
    }

    [TestCase("archive", false)]
    [TestCase("demote", false)]
    [TestCase("remove", false)]
    [TestCase("archive", true)]
    [TestCase("demote", true)]
    [TestCase("remove", true)]
    public async Task CompetingAccessChangesPrecedeMutation(string change, bool remove)
    {
        if (remove) await Create();
        await using var writer = _database.CreateContext();
        await writer.Database.OpenConnectionAsync();
        await using var transaction = ((SqliteConnection)writer.Database.GetDbConnection()).BeginTransaction(false);
        await writer.Database.UseTransactionAsync(transaction);
        writer.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = Guid.NewGuid(), Role = 0 });
        if (change == "archive") (await writer.Vaults.SingleAsync()).Status = 1;
        else if (change == "demote") (await writer.Memberships.SingleAsync()).Role = 3;
        else writer.Memberships.Remove(await writer.Memberships.SingleAsync());
        await writer.SaveChangesAsync();
        var pending = Task.Run(() => remove ? _store.RemoveAsync(_vault, _owner, _id, default) : Create());
        await transaction.CommitAsync();
        Assert.That(await pending, Is.EqualTo(change == "archive" ? RelationshipOutcome.Archived : change == "demote" ? RelationshipOutcome.Forbidden : RelationshipOutcome.Unavailable));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.Relationships.CountAsync(row => row.Status == 0), Is.EqualTo(remove ? 1 : 0));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedWritesRollBackWithoutDeletingEndpoints(bool remove)
    {
        if (remove) await Create();
        await using (var db = _database.CreateContext())
            await db.Database.ExecuteSqlRawAsync(remove
                ? "CREATE TRIGGER RejectRelationship BEFORE UPDATE ON Relationships BEGIN SELECT RAISE(ABORT, 'fictional'); END;"
                : "CREATE TRIGGER RejectRelationship BEFORE INSERT ON Relationships BEGIN SELECT RAISE(ABORT, 'fictional'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => remove ? _store.RemoveAsync(_vault, _owner, _id, default) : Create());
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.Relationships.CountAsync(row => row.Status == 0), Is.EqualTo(remove ? 1 : 0));
        Assert.That(await inspect.Assets.CountAsync(), Is.EqualTo(2));
    }

    [TestCase("UPDATE Relationships SET Kind = 99")]
    [TestCase("UPDATE Relationships SET Status = 99")]
    [TestCase("UPDATE Relationships SET Id = '00000000-0000-0000-0000-000000000000'")]
    [TestCase("UPDATE Relationships SET TargetAssetId = SourceAssetId")]
    [TestCase("UPDATE Relationships SET TargetAssetId = '11111111-1111-1111-1111-111111111111'")]
    [TestCase("DELETE FROM Assets")]
    public async Task DatabaseConstraintsProtectRootsAndEndpoints(string sql)
    {
        await Create();
        await using var db = _database.CreateContext();
        var failure = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.That(failure!.SqliteErrorCode, Is.EqualTo(19));
    }

    [Test]
    public async Task UniqueIndexRejectsRawActiveDuplicatesButAllowsRemovedTupleHistory()
    {
        await Create();
        await using (var db = _database.CreateContext())
        {
            db.Relationships.Add(new RelationshipRow { Id = Guid.NewGuid(), VaultId = _vault, SourceAssetId = _source, TargetAssetId = _target });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await _store.RemoveAsync(_vault, _owner, _id, default);
        Assert.That(await Create(Guid.NewGuid()), Is.EqualTo(RelationshipOutcome.Succeeded));
    }

    [Test]
    public async Task CorruptActualOwnershipFailsReadAndRemovalWithoutMutation()
    {
        await Create(); var other = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(other, "Other", VaultType.Personal, _owner).Vault!, default);
        await using (var db = _database.CreateContext())
        {
            (await db.Assets.SingleAsync(row => row.Id == _target)).VaultId = other;
            await db.SaveChangesAsync();
        }
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => _store.FindAsync(_vault, _owner, _id, default));
        Assert.That(failure!.Message, Is.EqualTo("Invalid stored Relationship ownership."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.RemoveAsync(_vault, _owner, _id, default));
        await using var inspect = _database.CreateContext();
        Assert.That((await inspect.Relationships.SingleAsync()).Status, Is.Zero);
    }

    [Test]
    public async Task UpgradeBackupRestorePreserveExistingDataAndActiveRemovedRoots()
    {
        var prior = new SqliteDatabase(Path.Combine(_directory, "prior.db"));
        var evidence = Guid.NewGuid();
        await using (var db = prior.CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261003124608_AddAssetEvidence");
            db.Vaults.Add(new VaultRow { Id = _vault, Name = "Vault", Type = 0, Status = 0 });
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = _owner, Role = 0 });
            db.Assets.AddRange(new AssetRow { Id = _source, VaultId = _vault, Name = "Source" }, new AssetRow { Id = _target, VaultId = _vault, Name = "Target" });
            db.AssetAttributes.Add(new AssetAttributeRow { AssetId = _source, Name = "Material", Value = "keep" });
            db.AssetEvidence.Add(new EvidenceRow { AssetId = _target, Id = evidence, Label = "Label", Kind = 1, Content = "keep" });
            db.Users.Add(new HomeVaultUser { Id = _owner, UserName = "owner@example.invalid", NormalizedUserName = "OWNER@EXAMPLE.INVALID", SecurityStamp = "fictional-stamp" });
            db.Set<AccountCredentialRow>().Add(new AccountCredentialRow { Hash = "fictional-hash", Login = "OWNER@EXAMPLE.INVALID", Purpose = "invitation", Expires = 123456, Consumed = true });
            await db.SaveChangesAsync();
        }
        await prior.MigrateAsync();
        var store = new SqliteRelationshipStore(prior); var active = Guid.NewGuid();
        Assert.That(await store.FindAsync(_vault, _owner, _id, default), Is.Null);
        await store.CreateAsync(_vault, _owner, _id, _source, _target, RelationshipKind.Covers, default);
        await store.RemoveAsync(_vault, _owner, _id, default);
        await store.CreateAsync(_vault, _owner, active, _source, _target, RelationshipKind.Covers, default);
        var backup = Path.Combine(_directory, "backup.db"); var restored = Path.Combine(_directory, "restored.db");
        await prior.CreateVerifiedCopyAsync(backup);
        await new SqliteDatabase(backup).CreateVerifiedCopyAsync(restored);
        foreach (var database in new[] { prior, new SqliteDatabase(backup), new SqliteDatabase(restored) })
        {
            var reader = new SqliteRelationshipStore(database);
            Assert.That((await reader.FindAsync(_vault, _owner, _id, default))!.Status, Is.EqualTo(RelationshipStatus.Removed));
            Assert.That((await reader.FindAsync(_vault, _owner, active, default))!.Status, Is.EqualTo(RelationshipStatus.Active));
            Assert.That((await new SqliteOrdinaryAttributeStore(database).ListAsync(_source, _owner, default))!.Single().Value, Is.EqualTo("keep"));
            Assert.That((await new SqliteEvidenceStore(database).ReadContentAsync(_target, _owner, evidence, default))!.ReadContent(), Is.EqualTo("keep"));
            await using var inspect = database.CreateContext();
            Assert.That((await inspect.Users.SingleAsync()).SecurityStamp, Is.EqualTo("fictional-stamp"));
            Assert.That((await inspect.Set<AccountCredentialRow>().SingleAsync()).Hash, Is.EqualTo("fictional-hash"));
        }
    }
}
