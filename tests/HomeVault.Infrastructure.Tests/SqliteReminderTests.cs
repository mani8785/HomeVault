using HomeVault.Application.Reminders;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Reminders;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

public sealed class SqliteReminderTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;
    private SqliteReminderStore _store = null!;
    private Guid _vault, _owner, _asset, _id;
    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVault-reminders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "reminders.db")); await _database.MigrateAsync();
        _store = new SqliteReminderStore(_database);
        _vault = Guid.NewGuid(); _owner = Guid.NewGuid(); _asset = Guid.NewGuid(); _id = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(_vault, "Vault", VaultType.Personal, _owner).Vault!, default);
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(_asset, _vault, "Asset").Asset!, _owner, default);
    }
    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);
    private Task<ReminderOutcome> Create(Guid? id = null, DateTimeOffset due = default) => _store.CreateAsync(_vault, _owner, id ?? _id, _asset, " fictional action ", due, default);
    private Task<ReminderSnapshot?> Read() => _store.FindAsync(_vault, _owner, _id, default);
    private Task<ReminderOutcome> Write(string operation, Guid? actor = null) => operation switch
    {
        "create" => _store.CreateAsync(_vault, actor ?? _owner, Guid.NewGuid(), _asset, "new", default, default),
        "update" => _store.UpdateAsync(_vault, actor ?? _owner, _id, "new", DateTimeOffset.MaxValue, default),
        "complete" => _store.CompleteAsync(_vault, actor ?? _owner, _id, default),
        _ => _store.CancelAsync(_vault, actor ?? _owner, _id, default)
    };
    [Test]
    public async Task ExactInstantsLifecycleFailuresAndIdentityRetention()
    {
        foreach (var instant in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.FromHours(-7)).AddTicks(1) })
        {
            var id = Guid.NewGuid(); Assert.That(await Create(id, instant), Is.EqualTo(ReminderOutcome.Succeeded));
            var root = await new SqliteReminderStore(_database).FindAsync(_vault, _owner, id, default);
            Assert.That(root!.DueAt.UtcTicks, Is.EqualTo(instant.UtcTicks)); Assert.That(root.DueAt.Offset, Is.EqualTo(TimeSpan.Zero));
        }
        await Create(); var before = (await Read())!;
        Assert.That(await _store.UpdateAsync(_vault, _owner, _id, " ", DateTimeOffset.MaxValue, default), Is.EqualTo(ReminderOutcome.BlankAction));
        Assert.That((await Read())!.DueAt, Is.EqualTo(before.DueAt));
        Assert.That(await Write("update"), Is.EqualTo(ReminderOutcome.Succeeded));
        Assert.That(before.ReadAction(), Is.EqualTo(" fictional action "));
        Assert.That(await Write("complete"), Is.EqualTo(ReminderOutcome.Succeeded));
        Assert.That(await Write("complete"), Is.EqualTo(ReminderOutcome.Succeeded));
        Assert.That(await Write("cancel"), Is.EqualTo(ReminderOutcome.NotPending));
        Assert.That(await _store.UpdateAsync(_vault, _owner, _id, null, default, default), Is.EqualTo(ReminderOutcome.NotPending));
        Assert.That(await Create(), Is.EqualTo(ReminderOutcome.IdentityConflict));
        Assert.That((await Read())!.ReadAction(), Is.EqualTo("new"));
        Assert.That((await Read())!.DueAt, Is.EqualTo(DateTimeOffset.MaxValue));
        var cancelled = Guid.NewGuid(); await Create(cancelled);
        Assert.That(await _store.CancelAsync(_vault, _owner, cancelled, default), Is.EqualTo(ReminderOutcome.Succeeded));
        Assert.That(await _store.CancelAsync(_vault, _owner, cancelled, default), Is.EqualTo(ReminderOutcome.Succeeded));
        Assert.That(await _store.CompleteAsync(_vault, _owner, cancelled, default), Is.EqualTo(ReminderOutcome.NotPending));
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task RolesArchiveAndRevocationApplyToAllOperations(int role)
    {
        await Create(); var actor = Guid.NewGuid();
        await using (var db = _database.CreateContext()) { db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = actor, Role = role }); await db.SaveChangesAsync(); }
        foreach (var operation in new[] { "create", "update", "complete" })
            Assert.That(await Write(operation, actor), Is.EqualTo(role == 3 ? ReminderOutcome.Forbidden : ReminderOutcome.Succeeded));
        Assert.That(await _store.FindAsync(_vault, actor, _id, default), Is.Not.Null);
        await new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default);
        foreach (var operation in new[] { "create", "update", "complete", "cancel" })
            Assert.That(await Write(operation, actor), Is.EqualTo(role == 3 ? ReminderOutcome.Forbidden : ReminderOutcome.Archived));
        Assert.That(await _store.FindAsync(_vault, actor, _id, default), Is.Not.Null);
        await using (var db = _database.CreateContext()) { db.Memberships.Remove(await db.Memberships.SingleAsync(row => row.ActorId == actor)); await db.SaveChangesAsync(); }
        Assert.That(await _store.FindAsync(_vault, actor, _id, default), Is.Null);
        Assert.That(await Write("update", actor), Is.EqualTo(ReminderOutcome.Unavailable));
    }
    [Test]
    public async Task CrossVaultAndCorruptOwnershipFailWithoutDisclosureOrMutation()
    {
        await Create(); var other = Guid.NewGuid(); var foreign = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(other, "Other", VaultType.Personal, _owner).Vault!, default);
        await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(foreign, other, "Other").Asset!, _owner, default);
        Assert.That(await _store.CreateAsync(_vault, _owner, Guid.NewGuid(), foreign, null, default, default), Is.EqualTo(ReminderOutcome.Unavailable));
        Assert.That(await _store.CreateAsync(other, _owner, _id, foreign, "action", default, default), Is.EqualTo(ReminderOutcome.Unavailable));
        Assert.That(await _store.FindAsync(other, _owner, _id, default), Is.Null);
        Assert.That(await _store.CancelAsync(other, _owner, _id, default), Is.EqualTo(ReminderOutcome.Unavailable));
        await using (var db = _database.CreateContext()) { (await db.Assets.SingleAsync(row => row.Id == _asset)).VaultId = other; await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Read());
        foreach (var operation in new[] { "update", "complete", "cancel" }) await Assert.ThrowsAsync<InvalidOperationException>(() => Write(operation));
        await using var inspect = _database.CreateContext(); Assert.That((await inspect.Reminders.SingleAsync()).Status, Is.Zero);
    }
    [Test]
    public async Task ConcurrentTerminalTransitionsAndUpdatesHaveSerialOutcomes()
    {
        await Create();
        var results = await Task.WhenAll(Task.Run(() => Write("complete")), Task.Run(() => Write("cancel")));
        Assert.That(results.Count(value => value == ReminderOutcome.Succeeded), Is.EqualTo(1));
        Assert.That(results.Count(value => value == ReminderOutcome.NotPending), Is.EqualTo(1));
        var id = Guid.NewGuid(); await Create(id);
        await Task.WhenAll(Task.Run(() => _store.UpdateAsync(_vault, _owner, id, "first", DateTimeOffset.MinValue, default)),
            Task.Run(() => _store.UpdateAsync(_vault, _owner, id, "second", DateTimeOffset.MaxValue, default)));
        var state = (await _store.FindAsync(_vault, _owner, id, default))!;
        Assert.That(state.DueAt, Is.EqualTo(state.ReadAction() == "first" ? DateTimeOffset.MinValue : DateTimeOffset.MaxValue));
        var duplicate = Guid.NewGuid();
        var creates = await Task.WhenAll(Task.Run(() => Create(duplicate)), Task.Run(() => Create(duplicate)));
        Assert.That(creates, Is.EquivalentTo(new[] { ReminderOutcome.Succeeded, ReminderOutcome.IdentityConflict }));
    }
    [TestCase("create")]
    [TestCase("update")]
    [TestCase("complete")]
    [TestCase("cancel")]
    public async Task CompetingPermissionAndArchiveChangesAreObserved(string operation)
    {
        await Create();
        foreach (var change in new[] { "demote", "archive", "remove" })
        {
            await using var writer = _database.CreateContext(); await writer.Database.OpenConnectionAsync();
            await using var transaction = ((SqliteConnection)writer.Database.GetDbConnection()).BeginTransaction(false);
            await writer.Database.UseTransactionAsync(transaction);
            var member = await writer.Memberships.SingleAsync(row => row.ActorId == _owner);
            var vault = await writer.Vaults.SingleAsync();
            member.Role = change == "demote" ? 3 : 0; vault.Status = change == "archive" ? 1 : 0;
            if (change == "remove") writer.Memberships.Remove(member);
            await writer.SaveChangesAsync();
            var pending = Task.Run(() => Write(operation)); await transaction.CommitAsync();
            Assert.That(await pending, Is.EqualTo(change == "demote" ? ReminderOutcome.Forbidden : change == "archive" ? ReminderOutcome.Archived : ReminderOutcome.Unavailable));
        }
        await using var inspect = _database.CreateContext(); var row = await inspect.Reminders.SingleAsync();
        Assert.That(row.Status, Is.Zero); Assert.That(row.Action, Is.EqualTo(" fictional action "));
    }
    [TestCase("create")]
    [TestCase("update")]
    [TestCase("complete")]
    [TestCase("cancel")]
    public async Task FailedWritesAndCancellationLeaveStateUnchanged(string operation)
    {
        await Create();
        await using (var db = _database.CreateContext()) await db.Database.ExecuteSqlRawAsync(operation == "create"
            ? "CREATE TRIGGER RejectReminder BEFORE INSERT ON Reminders BEGIN SELECT RAISE(ABORT, 'fictional'); END;"
            : "CREATE TRIGGER RejectReminder BEFORE UPDATE ON Reminders BEGIN SELECT RAISE(ABORT, 'fictional'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => Write(operation));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => _store.CompleteAsync(_vault, _owner, _id, cancellation.Token));
        var root = (await Read())!; Assert.That(root.Status, Is.EqualTo(ReminderStatus.Pending));
        Assert.That(root.ReadAction(), Is.EqualTo(" fictional action ")); Assert.That(root.DueAt, Is.EqualTo(DateTimeOffset.MinValue));
    }
    [TestCase("UPDATE Reminders SET DueAtUtcTicks = -1")]
    [TestCase("UPDATE Reminders SET DueAtUtcTicks = 3155378976000000000")]
    [TestCase("UPDATE Reminders SET DueAtUtcTicks = 0.5")]
    [TestCase("UPDATE Reminders SET Status = 99")]
    [TestCase("UPDATE Reminders SET Id = '00000000-0000-0000-0000-000000000000'")]
    [TestCase("UPDATE Reminders SET AssetId = '11111111-1111-1111-1111-111111111111'")]
    [TestCase("DELETE FROM Assets")]
    public async Task SchemaProtectsRangeIdentityStateAndAsset(string sql)
    {
        await Create(); await using var db = _database.CreateContext();
        var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql)); Assert.That(error!.SqliteErrorCode, Is.EqualTo(19));
    }
    [Test]
    public async Task UpgradeAndVerifiedBackupPreservePreviousDataAndAllReminderStates()
    {
        var prior = new SqliteDatabase(Path.Combine(_directory, "prior.db")); var target = Guid.NewGuid(); var relationship = Guid.NewGuid();
        await using (var db = prior.CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261004130707_AddRelationships");
            db.Vaults.Add(new VaultRow { Id = _vault, Name = "Vault" });
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = _owner });
            db.Assets.AddRange(new AssetRow { Id = _asset, VaultId = _vault, Name = "Asset" }, new AssetRow { Id = target, VaultId = _vault, Name = "Target" });
            db.AssetAttributes.Add(new AssetAttributeRow { AssetId = _asset, Name = "Material", Value = "keep" });
            db.AssetEvidence.Add(new EvidenceRow { AssetId = _asset, Id = Guid.NewGuid(), Label = "Label", Kind = 1, Content = "keep" });
            db.Relationships.Add(new RelationshipRow { Id = relationship, VaultId = _vault, SourceAssetId = _asset, TargetAssetId = target });
            db.Users.Add(new HomeVaultUser { Id = _owner, UserName = "owner@example.invalid", SecurityStamp = "fictional" });
            db.Set<AccountCredentialRow>().Add(new AccountCredentialRow { Hash = "fictional", Login = "OWNER@EXAMPLE.INVALID", Purpose = "invitation", Expires = 123456, Consumed = true });
            await db.SaveChangesAsync();
        }
        await prior.MigrateAsync(); var store = new SqliteReminderStore(prior); var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in ids) await store.CreateAsync(_vault, _owner, id, _asset, " preserved ", DateTimeOffset.MaxValue, default);
        await store.CompleteAsync(_vault, _owner, ids[1], default); await store.CancelAsync(_vault, _owner, ids[2], default);
        var backup = Path.Combine(_directory, "backup.db"); var restored = Path.Combine(_directory, "restored.db");
        await prior.CreateVerifiedCopyAsync(backup); await new SqliteDatabase(backup).CreateVerifiedCopyAsync(restored);
        foreach (var database in new[] { prior, new SqliteDatabase(backup), new SqliteDatabase(restored) })
        {
            var reader = new SqliteReminderStore(database);
            for (var index = 0; index < ids.Length; index++)
            {
                var root = (await reader.FindAsync(_vault, _owner, ids[index], default))!;
                Assert.That((int)root.Status, Is.EqualTo(index)); Assert.That(root.DueAt, Is.EqualTo(DateTimeOffset.MaxValue)); Assert.That(root.ReadAction(), Is.EqualTo(" preserved "));
            }
            await using var db = database.CreateContext();
            Assert.That((await db.AssetAttributes.SingleAsync()).Value, Is.EqualTo("keep")); Assert.That((await db.AssetEvidence.SingleAsync()).Content, Is.EqualTo("keep"));
            Assert.That((await db.Relationships.SingleAsync()).Id, Is.EqualTo(relationship)); Assert.That((await db.Users.SingleAsync()).SecurityStamp, Is.EqualTo("fictional"));
            Assert.That((await db.Set<AccountCredentialRow>().SingleAsync()).Hash, Is.EqualTo("fictional"));
        }
    }
}
