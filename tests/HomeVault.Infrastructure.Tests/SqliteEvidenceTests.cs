using System.Text.Json;
using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class SqliteEvidenceTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;
    private SqliteEvidenceStore _store = null!;
    private Guid _owner, _vault, _asset, _id;
    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVault-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "evidence.db"));
        await _database.MigrateAsync();
        _store = new SqliteEvidenceStore(_database);
        _owner = Guid.NewGuid(); _vault = Guid.NewGuid(); _asset = Guid.NewGuid(); _id = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(_vault, "Vault", VaultType.Personal, _owner).Vault!, default);
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(_asset, _vault, "Asset").Asset!, _owner, default);
    }
    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);
    private Task<EvidenceOutcome> Add(Guid? id = null, string label = " Label ", EvidenceKind kind = EvidenceKind.Note, string content = " Private sentinel \nمتن ") =>
        _store.AddAsync(_asset, _owner, id ?? _id, label, kind, content, default);

    [Test]
    public async Task RoundTripSnapshotsDuplicateRemovalReuseAndUnrelatedAttributes()
    {
        await new SqliteOrdinaryAttributeStore(_database).AddAsync(_asset, _owner, "Material", "keep", AttributeSensitivity.Ordinary, default);
        Assert.That(await Add(), Is.EqualTo(EvidenceOutcome.Succeeded));
        Assert.That(await Add(), Is.EqualTo(EvidenceOutcome.DuplicateIdentity));
        var other = Guid.NewGuid();
        Assert.That(await Add(other), Is.EqualTo(EvidenceOutcome.Succeeded));
        var before = await _store.ListAsync(_asset, _owner, default);
        var content = await _store.ReadContentAsync(_asset, _owner, _id, default);
        Assert.That(before!.Count, Is.EqualTo(2));
        Assert.That(before[0].Label, Is.EqualTo(" Label "));
        Assert.That(JsonSerializer.Serialize(before), Does.Not.Contain("Private sentinel"));
        Assert.That(content!.ReadContent(), Is.EqualTo(" Private sentinel \nمتن "));
        Assert.That(await _store.RemoveAsync(_asset, _owner, _id, default), Is.EqualTo(EvidenceOutcome.Succeeded));
        Assert.That(await _store.RemoveAsync(_asset, _owner, _id, default), Is.EqualTo(EvidenceOutcome.Unavailable));
        Assert.That(await _store.ReadContentAsync(_asset, _owner, _id, default), Is.Null);
        Assert.That(before.Count, Is.EqualTo(2));
        Assert.That(content.ReadContent(), Does.Contain("Private sentinel"));
        Assert.That(await Add(content: "https://example.invalid/a?x=fictional#part", kind: EvidenceKind.Url), Is.EqualTo(EvidenceOutcome.Succeeded));
        Assert.That((await _store.ReadContentAsync(_asset, _owner, _id, default))!.ReadContent(), Is.EqualTo("https://example.invalid/a?x=fictional#part"));
        Assert.That((await new SqliteOrdinaryAttributeStore(_database).ListAsync(_asset, _owner, default))!.Single().Value, Is.EqualTo("keep"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task AllRolesAndArchivedReadsAndWrites(int role)
    {
        await Add(); var actor = Guid.NewGuid();
        await using (var db = _database.CreateContext())
        {
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = actor, Role = role });
            await db.SaveChangesAsync();
        }
        var expected = role == 3 ? EvidenceOutcome.Forbidden : EvidenceOutcome.Succeeded;
        Assert.That(await _store.AddAsync(_asset, actor, Guid.NewGuid(), "Other", EvidenceKind.Note, "text", default), Is.EqualTo(expected));
        Assert.That(await _store.RemoveAsync(_asset, actor, _id, default), Is.EqualTo(expected));
        if (role != 3) await Add();
        await new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default);
        var archived = role == 3 ? EvidenceOutcome.Forbidden : EvidenceOutcome.Archived;
        Assert.That(await _store.AddAsync(_asset, actor, Guid.NewGuid(), "Other", EvidenceKind.Note, "text", default), Is.EqualTo(archived));
        Assert.That(await _store.RemoveAsync(_asset, actor, _id, default), Is.EqualTo(archived));
        Assert.That(await _store.ListAsync(_asset, actor, default), Is.Not.Null);
        Assert.That(await _store.ReadContentAsync(_asset, actor, _id, default), Is.Not.Null);
    }

    [Test]
    public async Task AssetLocalIdsCrossVaultIsolationAndMissingEquivalence()
    {
        await Add(); var outsider = Guid.NewGuid(); var vault = Guid.NewGuid(); var asset = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(vault, "Other", VaultType.Personal, outsider).Vault!, default);
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(asset, vault, "Other").Asset!, outsider, default);
        Assert.That(await _store.AddAsync(asset, outsider, _id, "Other", EvidenceKind.Note, "Other content", default), Is.EqualTo(EvidenceOutcome.Succeeded));
        foreach (var target in new[] { _asset, Guid.NewGuid() })
        {
            Assert.That(await _store.ListAsync(target, outsider, default), Is.Null);
            Assert.That(await _store.ReadContentAsync(target, outsider, _id, default), Is.Null);
            Assert.That(await _store.RemoveAsync(target, outsider, _id, default), Is.EqualTo(EvidenceOutcome.Unavailable));
            Assert.That(await _store.AddAsync(target, outsider, _id, " ", (EvidenceKind)99, " ", default), Is.EqualTo(EvidenceOutcome.Unavailable));
        }
        await _store.RemoveAsync(asset, outsider, _id, default);
        Assert.That(await _store.ReadContentAsync(_asset, _owner, _id, default), Is.Not.Null);
    }

    [TestCase("ftp://example.invalid/a")]
    [TestCase("file:///private")]
    [TestCase("https://user:password@example.invalid/")]
    [TestCase("https://example.invalid/raw space")]
    [TestCase("https://example.invalid/\n")]
    public async Task InvalidUrlsNeverPersist(string content)
    {
        Assert.That(await Add(kind: EvidenceKind.Url, content: content), Is.EqualTo(EvidenceOutcome.InvalidUrl));
        Assert.That(await _store.ListAsync(_asset, _owner, default), Is.Empty);
    }

    [Test]
    public async Task ValidationPrecedenceAndCancellationPreserveState()
    {
        await Add();
        Assert.That(await Add(label: " ", kind: (EvidenceKind)99, content: " "), Is.EqualTo(EvidenceOutcome.BlankLabel));
        Assert.That(await Add(kind: (EvidenceKind)99, content: " "), Is.EqualTo(EvidenceOutcome.InvalidKind));
        Assert.That(await Add(content: " "), Is.EqualTo(EvidenceOutcome.BlankContent));
        await Assert.ThrowsAsync<OperationCanceledException>(() => _store.RemoveAsync(_asset, _owner, _id, new CancellationToken(true)));
        Assert.That((await _store.ListAsync(_asset, _owner, default))!.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task MetadataAndUnauthorizedReadsNeverLoadContentColumn()
    {
        await Add();
        await using (var db = _database.CreateContext())
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE AssetEvidence RENAME COLUMN Content TO HiddenContent");
        Assert.That((await _store.ListAsync(_asset, _owner, default))!.Single().Id, Is.EqualTo(_id));
        Assert.That(await _store.ReadContentAsync(_asset, Guid.NewGuid(), _id, default), Is.Null);
        await Assert.ThrowsAsync<SqliteException>(() => _store.ReadContentAsync(_asset, _owner, _id, default));
    }

    [TestCase("UPDATE AssetEvidence SET Id = '00000000-0000-0000-0000-000000000000'")]
    [TestCase("UPDATE AssetEvidence SET Kind = 99")]
    [TestCase("UPDATE AssetEvidence SET AssetId = '11111111-1111-1111-1111-111111111111'")]
    public async Task DatabaseRejectsInvalidIdentityKindAndOrphan(string sql)
    {
        await Add();
        await using var db = _database.CreateContext();
        var failure = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.That(failure!.SqliteErrorCode, Is.EqualTo(19));
        Assert.That((await _store.ListAsync(_asset, _owner, default))!.Single().Id, Is.EqualTo(_id));
    }

    [Test]
    public async Task CorruptContentFailsDeliberateReadAndMutationButDoesNotLeakIntoMetadata()
    {
        await Add();
        await using (var db = _database.CreateContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE AssetEvidence SET Kind = 0, Content = 'Private sentinel'");
        Assert.That(await _store.ListAsync(_asset, _owner, default), Is.Not.Null);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ReadContentAsync(_asset, _owner, _id, default));
        Assert.That(failure!.Message, Is.EqualTo("Invalid stored Evidence state."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.RemoveAsync(_asset, _owner, _id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Add(Guid.NewGuid()));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.AssetEvidence.CountAsync(), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WriteFailuresPreserveExistingState(bool remove)
    {
        await Add();
        await using (var db = _database.CreateContext())
            await db.Database.ExecuteSqlRawAsync(remove
                ? "CREATE TRIGGER RejectEvidence BEFORE DELETE ON AssetEvidence BEGIN SELECT RAISE(ABORT, 'fictional'); END;"
                : "CREATE TRIGGER RejectEvidence BEFORE INSERT ON AssetEvidence BEGIN SELECT RAISE(ABORT, 'fictional'); END;");
        if (remove) await Assert.ThrowsAsync<SqliteException>(() => _store.RemoveAsync(_asset, _owner, _id, default));
        else await Assert.ThrowsAsync<DbUpdateException>(() => Add(Guid.NewGuid()));
        Assert.That((await _store.ListAsync(_asset, _owner, default))!.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ConcurrentDuplicateAndRemovalHaveSerializedOutcomes()
    {
        var adds = await Task.WhenAll(Task.Run(() => Add()), Task.Run(() => Add()));
        Assert.That(adds, Is.EquivalentTo(new[] { EvidenceOutcome.Succeeded, EvidenceOutcome.DuplicateIdentity }));
        var removes = await Task.WhenAll(Task.Run(() => _store.RemoveAsync(_asset, _owner, _id, default)), Task.Run(() => _store.RemoveAsync(_asset, _owner, _id, default)));
        Assert.That(removes, Is.EquivalentTo(new[] { EvidenceOutcome.Succeeded, EvidenceOutcome.Unavailable }));
    }

    [TestCase("archive", false)]
    [TestCase("demote", false)]
    [TestCase("remove", false)]
    [TestCase("archive", true)]
    [TestCase("demote", true)]
    [TestCase("remove", true)]
    public async Task CompetingPermissionChangeIsObservedBeforeWrite(string change, bool remove)
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
        var pending = Task.Run(() => remove ? _store.RemoveAsync(_asset, _owner, _id, default) : Add(Guid.NewGuid()));
        await transaction.CommitAsync();
        Assert.That(await pending, Is.EqualTo(change == "archive" ? EvidenceOutcome.Archived : change == "demote" ? EvidenceOutcome.Forbidden : EvidenceOutcome.Unavailable));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.AssetEvidence.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task UpgradeBackupAndRestorePreserveAccountsCredentialsAttributesAndEvidence()
    {
        var prior = new SqliteDatabase(Path.Combine(_directory, "prior.db"));
        await using (var db = prior.CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261003112737_AddOrdinaryAttributes");
            db.Vaults.Add(new VaultRow { Id = _vault, Name = "Vault", Type = 0, Status = 0 });
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = _owner, Role = 0 });
            db.Assets.Add(new AssetRow { Id = _asset, VaultId = _vault, Name = "Asset" });
            db.AssetAttributes.Add(new AssetAttributeRow { AssetId = _asset, Name = "Material", Value = "keep" });
            db.Users.Add(new HomeVaultUser { Id = _owner, UserName = "owner@example.invalid", NormalizedUserName = "OWNER@EXAMPLE.INVALID", SecurityStamp = "fictional-stamp" });
            db.Set<AccountCredentialRow>().Add(new AccountCredentialRow { Hash = "fictional-hash", Login = "OWNER@EXAMPLE.INVALID", Purpose = "invitation", Expires = 123456, Consumed = true });
            await db.SaveChangesAsync();
        }
        await prior.MigrateAsync();
        var store = new SqliteEvidenceStore(prior);
        Assert.That(await store.ListAsync(_asset, _owner, default), Is.Empty);
        await store.AddAsync(_asset, _owner, _id, "Label", EvidenceKind.Note, " preserved ", default);
        var backup = Path.Combine(_directory, "backup.db"); var restored = Path.Combine(_directory, "restored.db");
        await prior.CreateVerifiedCopyAsync(backup);
        await new SqliteDatabase(backup).CreateVerifiedCopyAsync(restored);
        foreach (var database in new[] { prior, new SqliteDatabase(backup), new SqliteDatabase(restored) })
        {
            Assert.That((await new SqliteEvidenceStore(database).ReadContentAsync(_asset, _owner, _id, default))!.ReadContent(), Is.EqualTo(" preserved "));
            Assert.That((await new SqliteOrdinaryAttributeStore(database).ListAsync(_asset, _owner, default))!.Single().Value, Is.EqualTo("keep"));
            await using var inspect = database.CreateContext();
            Assert.That((await inspect.Users.SingleAsync()).SecurityStamp, Is.EqualTo("fictional-stamp"));
            Assert.That((await inspect.Set<AccountCredentialRow>().SingleAsync()).Hash, Is.EqualTo("fictional-hash"));
        }
    }
}
