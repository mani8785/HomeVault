using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
public sealed class SqliteVaultMembershipTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;
    private SqliteVaultMembershipStore _store = null!;
    private Guid _owner, _vault, _target;

    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVault-members-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "members.db"));
        await _database.MigrateAsync();
        _store = new SqliteVaultMembershipStore(_database);
        _owner = Guid.NewGuid();
        _target = Guid.NewGuid();
        _vault = Guid.NewGuid();
        await new SqliteVaultRepository(_database).AddAsync(Vault.Create(_vault, "Fictional", VaultType.Personal, _owner).Vault!, default);
        await using var db = _database.CreateContext();
        db.Users.Add(new HomeVaultUser { Id = _target });
        await db.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public async Task AddsChangesRemovesAndPreservesDisabledAccountCleanup()
    {
        Assert.That(await _store.AddAsync(_vault, _owner, _target, VaultRole.Editor, default), Is.EqualTo(MembershipOutcome.Succeeded));
        Assert.That(await _store.AddAsync(_vault, _owner, _target, VaultRole.Viewer, default), Is.EqualTo(MembershipOutcome.DuplicateMember));
        await using (var db = _database.CreateContext())
        {
            (await db.Users.SingleAsync()).IsEnabled = false;
            await db.SaveChangesAsync();
        }
        Assert.That(await _store.ChangeRoleAsync(_vault, _owner, _target, VaultRole.Viewer, default), Is.EqualTo(MembershipOutcome.Succeeded));
        Assert.That(await _store.ChangeRoleAsync(_vault, _owner, _target, VaultRole.Viewer, default), Is.EqualTo(MembershipOutcome.Succeeded));
        Assert.That(await _store.RemoveAsync(_vault, _owner, _target, default), Is.EqualTo(MembershipOutcome.Succeeded));
        Assert.That(await _store.RemoveAsync(_vault, _owner, _target, default), Is.EqualTo(MembershipOutcome.Unavailable));
        Assert.That(await _store.AddAsync(_vault, _owner, _target, VaultRole.Viewer, default), Is.EqualTo(MembershipOutcome.Unavailable));
        Assert.That(await _store.AddAsync(_vault, _owner, Guid.NewGuid(), VaultRole.Viewer, default), Is.EqualTo(MembershipOutcome.Unavailable));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.Memberships.CountAsync(), Is.EqualTo(1));
        Assert.That(await inspect.Users.CountAsync(), Is.EqualTo(1));
    }

    [Test, Combinatorial]
    public async Task EnforcesFullRoleTransitionMatrix([Values(0, 1, 2, 3)] int requesterRole,
        [Values(0, 1, 2, 3)] int oldRole, [Values(0, 1, 2, 3)] int newRole)
    {
        var requester = Guid.NewGuid();
        await using (var db = _database.CreateContext())
        {
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = requester, Role = requesterRole });
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = _target, Role = oldRole });
            await db.SaveChangesAsync();
        }
        var allowed = requesterRole == 0 || (requesterRole == 1 && oldRole >= 2 && newRole >= 2);
        Assert.That(await _store.ChangeRoleAsync(_vault, requester, _target, (VaultRole)newRole, default),
            Is.EqualTo(allowed ? MembershipOutcome.Succeeded : MembershipOutcome.Forbidden));
        await using var inspect = _database.CreateContext();
        Assert.That((await inspect.Memberships.SingleAsync(member => member.ActorId == _target)).Role, Is.EqualTo(allowed ? newRole : oldRole));
    }

    [Test, Combinatorial]
    public async Task EnforcesAdditionAndRemovalMatrix([Values(0, 1, 2, 3)] int requesterRole, [Values(0, 1, 2, 3)] int targetRole)
    {
        var requester = Guid.NewGuid();
        await using (var db = _database.CreateContext())
        {
            db.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = requester, Role = requesterRole });
            await db.SaveChangesAsync();
        }
        var allowed = requesterRole == 0 || (requesterRole == 1 && targetRole >= 2);
        Assert.That(await _store.AddAsync(_vault, requester, _target, (VaultRole)targetRole, default),
            Is.EqualTo(allowed ? MembershipOutcome.Succeeded : MembershipOutcome.Forbidden));
        if (!allowed)
        {
            await using var setup = _database.CreateContext();
            setup.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = _target, Role = targetRole });
            await setup.SaveChangesAsync();
        }
        Assert.That(await _store.RemoveAsync(_vault, requester, _target, default),
            Is.EqualTo(allowed ? MembershipOutcome.Succeeded : MembershipOutcome.Forbidden));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.Memberships.AnyAsync(member => member.ActorId == _target), Is.EqualTo(!allowed));
    }

    [Test]
    public async Task ArchivedAndInaccessibleOperationsCannotChangeMembership()
    {
        await _store.AddAsync(_vault, _owner, _target, VaultRole.Viewer, default);
        await new SqliteVaultArchiveStore(_database).ArchiveAsync(_vault, _owner, default);
        Assert.That(await _store.AddAsync(_vault, _owner, Guid.NewGuid(), VaultRole.Viewer, default), Is.EqualTo(MembershipOutcome.Archived));
        Assert.That(await _store.ChangeRoleAsync(_vault, _owner, _target, VaultRole.Viewer, default), Is.EqualTo(MembershipOutcome.Archived));
        Assert.That(await _store.RemoveAsync(_vault, _owner, _target, default), Is.EqualTo(MembershipOutcome.Archived));
        Assert.That(await _store.RemoveAsync(_vault, _target, _owner, default), Is.EqualTo(MembershipOutcome.Forbidden));
        Assert.That(await _store.RemoveAsync(_vault, Guid.NewGuid(), _owner, default), Is.EqualTo(MembershipOutcome.Unavailable));
        Assert.That(await _store.RemoveAsync(Guid.NewGuid(), _owner, _target, default), Is.EqualTo(MembershipOutcome.Unavailable));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.Memberships.CountAsync(), Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LastOwnerSurvivesCompetingOwners(bool demote)
    {
        await _store.AddAsync(_vault, _owner, _target, VaultRole.Owner, default);
        Task<MembershipOutcome> Attempt(Guid requester, Guid target) => demote
            ? _store.ChangeRoleAsync(_vault, requester, target, VaultRole.Viewer, default)
            : _store.RemoveAsync(_vault, requester, target, default);
        var results = await Task.WhenAll(Task.Run(() => Attempt(_owner, _target)), Task.Run(() => Attempt(_target, _owner)));
        Assert.That(results.Count(result => result == MembershipOutcome.Succeeded), Is.EqualTo(1));
        Assert.That(results.Count(result => result == (demote ? MembershipOutcome.Forbidden : MembershipOutcome.Unavailable)), Is.EqualTo(1));
        await using var inspect = _database.CreateContext();
        var remaining = await inspect.Memberships.SingleAsync(member => member.Role == 0);
        Assert.That(await Attempt(remaining.ActorId, remaining.ActorId), Is.EqualTo(MembershipOutcome.LastOwner));
        Assert.That(await _store.ChangeRoleAsync(_vault, remaining.ActorId, remaining.ActorId, VaultRole.Owner, default), Is.EqualTo(MembershipOutcome.Succeeded));
    }

    [Test]
    public async Task ConcurrentAdditionsCommitOnlyOneMembership()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            _store.AddAsync(_vault, _owner, _target, VaultRole.Viewer, default))));
        Assert.That(outcomes.Count(value => value == MembershipOutcome.Succeeded), Is.EqualTo(1));
        Assert.That(outcomes.Count(value => value == MembershipOutcome.DuplicateMember), Is.EqualTo(3));
    }

    [TestCase("archive")]
    [TestCase("demote")]
    [TestCase("disable")]
    public async Task ObservesCompetingStateChanges(string change)
    {
        await using var writer = _database.CreateContext();
        await writer.Database.OpenConnectionAsync();
        await using var transaction = ((SqliteConnection)writer.Database.GetDbConnection()).BeginTransaction(false);
        await writer.Database.UseTransactionAsync(transaction);
        if (change == "archive") (await writer.Vaults.SingleAsync()).Status = 1;
        if (change == "disable") (await writer.Users.SingleAsync()).IsEnabled = false;
        if (change == "demote")
        {
            (await writer.Memberships.SingleAsync()).Role = 2;
            writer.Memberships.Add(new MembershipRow { VaultId = _vault, ActorId = Guid.NewGuid(), Role = 0 });
        }
        await writer.SaveChangesAsync();
        var pending = Task.Run(() => _store.AddAsync(_vault, _owner, _target, VaultRole.Editor, default));
        await transaction.CommitAsync();
        Assert.That(await pending, Is.EqualTo(change switch
        {
            "archive" => MembershipOutcome.Archived,
            "demote" => MembershipOutcome.Forbidden,
            _ => MembershipOutcome.Unavailable
        }));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.Memberships.AnyAsync(member => member.ActorId == _target), Is.False);
    }

    [TestCase("INSERT")]
    [TestCase("UPDATE")]
    [TestCase("DELETE")]
    public async Task FailedMutationRollsBackAndCancellationDoesNotWrite(string operation)
    {
        if (operation != "INSERT") await _store.AddAsync(_vault, _owner, _target, VaultRole.Viewer, default);
        await Assert.ThrowsAsync<OperationCanceledException>(() => _store.AddAsync(_vault, _owner, _target, VaultRole.Viewer, new CancellationToken(true)));
        await using (var setup = _database.CreateContext())
            await setup.Database.ExecuteSqlRawAsync(operation switch
            {
                "INSERT" => "CREATE TRIGGER FailMutation BEFORE INSERT ON Memberships BEGIN SELECT RAISE(ABORT, 'fictional failure'); END;",
                "UPDATE" => "CREATE TRIGGER FailMutation BEFORE UPDATE ON Memberships BEGIN SELECT RAISE(ABORT, 'fictional failure'); END;",
                _ => "CREATE TRIGGER FailMutation BEFORE DELETE ON Memberships BEGIN SELECT RAISE(ABORT, 'fictional failure'); END;"
            });
        await Assert.ThrowsAsync<DbUpdateException>(() => operation switch
        {
            "INSERT" => _store.AddAsync(_vault, _owner, _target, VaultRole.Viewer, default),
            "UPDATE" => _store.ChangeRoleAsync(_vault, _owner, _target, VaultRole.Editor, default),
            _ => _store.RemoveAsync(_vault, _owner, _target, default)
        });
        await using var inspect = _database.CreateContext();
        var target = await inspect.Memberships.SingleOrDefaultAsync(member => member.ActorId == _target);
        if (operation == "INSERT") Assert.That(target, Is.Null);
        else Assert.That(target!.Role, Is.EqualTo(3));
    }
}
