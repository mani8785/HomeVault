using HomeVault.Application.Vaults;
using HomeVault.Application.Identity;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Vaults;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
public sealed class InMemoryVaultRepositoryTests
{
    [TestCase(VaultType.Personal)]
    [TestCase(VaultType.Household)]
    [TestCase(VaultType.Organization)]
    public async Task StoresIndependentCreationSnapshot(VaultType type)
    {
        var repository = new InMemoryVaultRepository();
        var owner = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), " Example ", type, owner).Vault!;
        Assert.That(repository.Inspect(vault.Id), Is.Null);
        Assert.That(await repository.AddAsync(vault, default), Is.EqualTo(VaultAddOutcome.Added));
        vault.AddMember(Guid.NewGuid(), VaultRole.Editor);
        vault.Archive(owner);
        var stored = repository.Inspect(vault.Id)!;
        Assert.That(stored.Id, Is.EqualTo(vault.Id));
        Assert.That(stored.Name, Is.EqualTo(" Example "));
        Assert.That(stored.Type, Is.EqualTo(type));
        Assert.That(stored.Status, Is.EqualTo(VaultStatus.Active));
        Assert.That(stored.OwnerId, Is.EqualTo(owner));
        Assert.That(stored.ToString(), Is.EqualTo("StoredVault"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DuplicateNeverOverwritesStoredData(bool differentOwner)
    {
        var repository = new InMemoryVaultRepository();
        var original = NewVault();
        await repository.AddAsync(original, default);
        var before = repository.Inspect(original.Id);
        var duplicate = differentOwner
            ? Vault.Create(original.Id, "Replacement", VaultType.Organization, Guid.NewGuid()).Vault!
            : original;
        Assert.That(await repository.AddAsync(duplicate, default), Is.EqualTo(VaultAddOutcome.IdentityConflict));
        Assert.That(repository.Inspect(original.Id), Is.SameAs(before));
    }

    [Test]
    public async Task ConcurrentDuplicatesHaveExactlyOneWinner()
    {
        var repository = new InMemoryVaultRepository();
        var id = Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contenders = Enumerable.Range(0, 32).Select(_ => NewVault(id)).ToArray();
        var operations = contenders.Select(vault => Task.Run(async () =>
        {
            await start.Task;
            return await repository.AddAsync(vault, default);
        })).ToArray();
        start.SetResult();
        var outcomes = await Task.WhenAll(operations);
        Assert.That(outcomes.Count(outcome => outcome == VaultAddOutcome.Added), Is.EqualTo(1));
        Assert.That(outcomes.Count(outcome => outcome == VaultAddOutcome.IdentityConflict), Is.EqualTo(31));
        var winner = contenders[Array.IndexOf(outcomes, VaultAddOutcome.Added)];
        Assert.That(repository.Inspect(id)!.OwnerId, Is.EqualTo(winner.Memberships.Single().ActorId));
    }

    [Test]
    public async Task SeparateInstancesHaveIndependentLifetimes()
    {
        var vault = NewVault();
        var first = new InMemoryVaultRepository();
        var second = new InMemoryVaultRepository();
        await first.AddAsync(vault, default);
        Assert.That(second.Inspect(vault.Id), Is.Null);
        Assert.That(await second.AddAsync(vault, default), Is.EqualTo(VaultAddOutcome.Added));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidCreationStateDoesNotWrite(bool archived)
    {
        var vault = NewVault();
        if (archived)
            vault.Archive(vault.Memberships.Single().ActorId);
        else
            vault.AddMember(Guid.NewGuid(), VaultRole.Editor);
        var repository = new InMemoryVaultRepository();
        await Assert.ThrowsAsync<ArgumentException>(async () => await repository.AddAsync(vault, default));
        Assert.That(repository.Inspect(vault.Id), Is.Null);
    }

    [Test]
    public async Task NullIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await new InMemoryVaultRepository().AddAsync(null!, default));
    }

    [Test]
    public async Task PreCancellationDoesNotReserveIdentity()
    {
        var repository = new InMemoryVaultRepository();
        var vault = NewVault();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await repository.AddAsync(vault, new CancellationToken(true)));
        Assert.That(repository.Inspect(vault.Id), Is.Null);
        Assert.That(await repository.AddAsync(vault, default), Is.EqualTo(VaultAddOutcome.Added));
    }

    [Test]
    public async Task ApplicationInstancesShareStorageWithoutOverwritingAnotherOwner()
    {
        var repository = new InMemoryVaultRepository();
        var firstOwner = Guid.NewGuid();
        var request = new CreateVaultRequest(Guid.NewGuid(), "Example", VaultType.Household);
        var first = await new CreateVaultUseCase(new Actor(firstOwner), repository).ExecuteAsync(request);
        var second = await new CreateVaultUseCase(new Actor(Guid.NewGuid()), repository).ExecuteAsync(request);
        Assert.That(first.IsSuccess, Is.True);
        Assert.That(repository.Inspect(request.Id)!.OwnerId, Is.EqualTo(firstOwner));
        Assert.That(second.Error, Is.EqualTo(CreateVaultError.IdentityConflict));
        Assert.That(second.Vault, Is.Null);
    }

    private sealed class Actor(Guid actorId) : ICurrentActor
    {
        public Guid? ActorId => actorId;
    }

    private static Vault NewVault(Guid? id = null) =>
        Vault.Create(id ?? Guid.NewGuid(), "Example", VaultType.Personal, Guid.NewGuid()).Vault!;
}
