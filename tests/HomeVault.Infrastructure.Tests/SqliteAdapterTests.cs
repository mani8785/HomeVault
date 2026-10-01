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
public sealed class SqliteAdapterTests
{
    private string _directory = null!;
    private SqliteDatabase _database = null!;
    private Guid _owner;
    private Vault _vault = null!;

    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVaultTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _database = new SqliteDatabase(Path.Combine(_directory, "adapters.db"));
        await _database.MigrateAsync();
        _owner = Guid.NewGuid();
        _vault = Vault.Create(Guid.NewGuid(), " Original ", VaultType.Household, _owner).Vault!;
        await new SqliteVaultRepository(_database).AddAsync(_vault, default);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public async Task ReopensIndependentSnapshotsAndPreservesOriginalOwnerOnDuplicate()
    {
        var duplicate = Vault.Create(_vault.Id, "Replacement", VaultType.Personal, Guid.NewGuid()).Vault!;
        Assert.That(await new SqliteVaultRepository(_database).AddAsync(duplicate, default), Is.EqualTo(VaultAddOutcome.IdentityConflict));
        var asset = Asset.Create(Guid.NewGuid(), _vault.Id, " Bicycle ").Asset!;
        Assert.That(await new SqliteAssetRegistrationStore(_database).RegisterAsync(asset, _owner, default), Is.EqualTo(AssetRegistrationOutcome.Added));
        _vault.Archive(_owner);
        asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary);
        await using var reopened = _database.CreateContext();
        Assert.That((await reopened.Vaults.SingleAsync()).Name, Is.EqualTo(" Original "));
        Assert.That((await reopened.Vaults.SingleAsync()).Status, Is.Zero);
        Assert.That((await reopened.Memberships.SingleAsync()).ActorId, Is.EqualTo(_owner));
        Assert.That((await reopened.Assets.SingleAsync()).Name, Is.EqualTo(" Bicycle "));
    }

    [TestCase(0, 0, AssetRegistrationOutcome.Added)]
    [TestCase(1, 0, AssetRegistrationOutcome.Added)]
    [TestCase(2, 0, AssetRegistrationOutcome.Added)]
    [TestCase(3, 0, AssetRegistrationOutcome.Forbidden)]
    [TestCase(0, 1, AssetRegistrationOutcome.VaultArchived)]
    [TestCase(1, 1, AssetRegistrationOutcome.VaultArchived)]
    [TestCase(2, 1, AssetRegistrationOutcome.VaultArchived)]
    [TestCase(3, 1, AssetRegistrationOutcome.Forbidden)]
    public async Task EnforcesStoredRoleAndState(int role, int status, AssetRegistrationOutcome expected)
    {
        var actor = Guid.NewGuid();
        await using (var setup = _database.CreateContext())
        {
            setup.Memberships.Add(new MembershipRow { VaultId = _vault.Id, ActorId = actor, Role = role });
            (await setup.Vaults.SingleAsync()).Status = status;
            await setup.SaveChangesAsync();
        }
        var outcome = await new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(Guid.NewGuid(), _vault.Id, "Example").Asset!, actor, default);
        Assert.That(outcome, Is.EqualTo(expected));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.Assets.CountAsync(), Is.EqualTo(expected == AssetRegistrationOutcome.Added ? 1 : 0));
    }

    [Test]
    public async Task MissingAndInaccessiblePrecedeCrossVaultIdentityConflict()
    {
        var adapter = new SqliteAssetRegistrationStore(_database);
        var id = Guid.NewGuid();
        await adapter.RegisterAsync(Asset.Create(id, _vault.Id, "Original").Asset!, _owner, default);
        var other = Vault.Create(Guid.NewGuid(), "Other", VaultType.Personal, _owner).Vault!;
        await new SqliteVaultRepository(_database).AddAsync(other, default);
        Assert.That(await adapter.RegisterAsync(Asset.Create(id, other.Id, "Replacement").Asset!, Guid.NewGuid(), default), Is.EqualTo(AssetRegistrationOutcome.VaultUnavailable));
        Assert.That(await adapter.RegisterAsync(Asset.Create(id, Guid.NewGuid(), "Replacement").Asset!, _owner, default), Is.EqualTo(AssetRegistrationOutcome.VaultUnavailable));
        Assert.That(await adapter.RegisterAsync(Asset.Create(id, other.Id, "Replacement").Asset!, _owner, default), Is.EqualTo(AssetRegistrationOutcome.IdentityConflict));
        await using var inspect = _database.CreateContext();
        Assert.That((await inspect.Assets.SingleAsync()).VaultId, Is.EqualTo(_vault.Id));
    }

    [Test]
    public async Task IndependentConnectionsSerializeDuplicateInserts()
    {
        var id = Guid.NewGuid();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(id, _vault.Id, "Example").Asset!, _owner, default))));
        Assert.That(outcomes.Count(value => value == AssetRegistrationOutcome.Added), Is.EqualTo(1));
        Assert.That(outcomes.Count(value => value == AssetRegistrationOutcome.IdentityConflict), Is.EqualTo(7));
        var vaultId = Guid.NewGuid();
        var vaults = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            new SqliteVaultRepository(_database).AddAsync(Vault.Create(vaultId, "Example", VaultType.Personal, Guid.NewGuid()).Vault!, default))));
        Assert.That(vaults.Count(value => value == VaultAddOutcome.Added), Is.EqualTo(1));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.Memberships.CountAsync(row => row.VaultId == vaultId), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RegistrationObservesCompetingArchiveOrMembershipRemoval(bool removeMember)
    {
        await using var writer = _database.CreateContext();
        await writer.Database.OpenConnectionAsync();
        await using var transaction = ((SqliteConnection)writer.Database.GetDbConnection()).BeginTransaction(false);
        await writer.Database.UseTransactionAsync(transaction);
        if (removeMember) writer.Memberships.Remove(await writer.Memberships.SingleAsync());
        else (await writer.Vaults.SingleAsync()).Status = 1;
        await writer.SaveChangesAsync();
        var pending = Task.Run(() => new SqliteAssetRegistrationStore(_database).RegisterAsync(Asset.Create(Guid.NewGuid(), _vault.Id, "Example").Asset!, _owner, default));
        await transaction.CommitAsync();
        Assert.That(await pending, Is.EqualTo(removeMember ? AssetRegistrationOutcome.VaultUnavailable : AssetRegistrationOutcome.VaultArchived));
    }

    [Test]
    public async Task FailedSecondInsertRollsBackVaultAndPropagatesFailure()
    {
        await using (var setup = _database.CreateContext())
            await setup.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailMembership BEFORE INSERT ON Memberships BEGIN SELECT RAISE(ABORT, 'Forced test failure'); END;");
        var candidate = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, Guid.NewGuid()).Vault!;
        await Assert.ThrowsAsync<DbUpdateException>(() => new SqliteVaultRepository(_database).AddAsync(candidate, default));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.Vaults.AnyAsync(row => row.Id == candidate.Id), Is.False);
    }

    [Test]
    public async Task PreCancellationAndInvalidStateDoNotWrite()
    {
        var adapter = new SqliteAssetRegistrationStore(_database);
        var asset = Asset.Create(Guid.NewGuid(), _vault.Id, "Example").Asset!;
        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.RegisterAsync(asset, _owner, new CancellationToken(true)));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.RegisterAsync(Asset.Create(Guid.NewGuid(), "Example").Asset!, _owner, default));
        asset.AddEvidence(Guid.NewGuid(), "Example", EvidenceKind.Note, "Example");
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.RegisterAsync(asset, _owner, default));
        _vault.Archive(_owner);
        await Assert.ThrowsAsync<ArgumentException>(() => new SqliteVaultRepository(_database).AddAsync(_vault, default));
        await using var inspect = _database.CreateContext();
        Assert.That(await inspect.Assets.CountAsync(), Is.Zero);
    }
}
