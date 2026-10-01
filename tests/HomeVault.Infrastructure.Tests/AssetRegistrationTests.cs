using HomeVault.Application.Assets;
using HomeVault.Application.Identity;
using HomeVault.Application.Vaults;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Assets;
using HomeVault.Infrastructure.Vaults;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
public sealed class AssetRegistrationTests
{
    [Test]
    public async Task ConnectedJourneyStoresBoundAssetAndIndependentSnapshot()
    {
        var memory = new InMemoryHomeVaultStore();
        var actor = new Actor(Guid.NewGuid());
        var vault = await new CreateVaultUseCase(actor, new InMemoryVaultRepository(memory))
            .ExecuteAsync(new CreateVaultRequest(Guid.NewGuid(), "Example", VaultType.Household));
        var adapter = new InMemoryAssetRegistrationStore(memory);
        var useCase = new RegisterAssetUseCase(actor, adapter);
        var request = new RegisterAssetRequest(Guid.NewGuid(), vault.Vault!.Id, " Bicycle ");
        var result = await useCase.ExecuteAsync(request);
        Assert.That(result.IsSuccess, Is.True);
        var stored = adapter.Inspect(request.Id)!;
        Assert.That(stored.VaultId, Is.EqualTo(vault.Vault.Id));
        Assert.That(stored.Name, Is.EqualTo(" Bicycle "));
        Assert.That(stored.ToString(), Is.EqualTo("StoredAsset"));
        Assert.That((await useCase.ExecuteAsync(request)).Error, Is.EqualTo(RegisterAssetError.IdentityConflict));
        Assert.That(adapter.Inspect(request.Id), Is.SameAs(stored));
        var separate = new InMemoryAssetRegistrationStore(new InMemoryHomeVaultStore());
        Assert.That(separate.Inspect(request.Id), Is.Null);
        Assert.That(await separate.RegisterAsync(Asset.Create(Guid.NewGuid(), request.VaultId, "Example").Asset!, actor.ActorId!.Value, default),
            Is.EqualTo(AssetRegistrationOutcome.VaultUnavailable));
    }

    [TestCase(VaultRole.Owner, false, AssetRegistrationOutcome.Added)]
    [TestCase(VaultRole.Administrator, false, AssetRegistrationOutcome.Added)]
    [TestCase(VaultRole.Editor, false, AssetRegistrationOutcome.Added)]
    [TestCase(VaultRole.Viewer, false, AssetRegistrationOutcome.Forbidden)]
    [TestCase(VaultRole.Owner, true, AssetRegistrationOutcome.VaultArchived)]
    [TestCase(VaultRole.Administrator, true, AssetRegistrationOutcome.VaultArchived)]
    [TestCase(VaultRole.Editor, true, AssetRegistrationOutcome.VaultArchived)]
    [TestCase(VaultRole.Viewer, true, AssetRegistrationOutcome.Forbidden)]
    public async Task EnforcesRoleAndArchiveWithoutPartialWrites(VaultRole role, bool archived, AssetRegistrationOutcome expected)
    {
        var owner = Guid.NewGuid();
        var actor = role == VaultRole.Owner ? owner : Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, owner).Vault!;
        if (actor != owner) vault.AddMember(actor, role);
        if (archived) vault.Archive(owner);
        var memory = new InMemoryHomeVaultStore();
        memory.SeedVault(vault);
        var adapter = new InMemoryAssetRegistrationStore(memory);
        var asset = Asset.Create(Guid.NewGuid(), vault.Id, "Example").Asset!;
        Assert.That(await adapter.RegisterAsync(asset, actor, default), Is.EqualTo(expected));
        Assert.That(adapter.Inspect(asset.Id) is not null, Is.EqualTo(expected == AssetRegistrationOutcome.Added));
    }

    [Test]
    public async Task AccessPrecedesGlobalConflictAndCrossVaultDuplicatesNeverMoveAssets()
    {
        var owner = Guid.NewGuid();
        var memory = new InMemoryHomeVaultStore();
        var first = Vault.Create(Guid.NewGuid(), "First", VaultType.Personal, owner).Vault!;
        var second = Vault.Create(Guid.NewGuid(), "Second", VaultType.Personal, owner).Vault!;
        memory.SeedVault(first);
        memory.SeedVault(second);
        var adapter = new InMemoryAssetRegistrationStore(memory);
        var original = Asset.Create(Guid.NewGuid(), first.Id, "Original").Asset!;
        await adapter.RegisterAsync(original, owner, default);
        var snapshot = adapter.Inspect(original.Id);
        var duplicate = Asset.Create(original.Id, second.Id, "Replacement").Asset!;
        Assert.That(await adapter.RegisterAsync(duplicate, Guid.NewGuid(), default), Is.EqualTo(AssetRegistrationOutcome.VaultUnavailable));
        Assert.That(await adapter.RegisterAsync(Asset.Create(original.Id, Guid.NewGuid(), "Example").Asset!, owner, default), Is.EqualTo(AssetRegistrationOutcome.VaultUnavailable));
        var viewer = Guid.NewGuid();
        second.AddMember(viewer, VaultRole.Viewer);
        memory.SeedVault(second);
        Assert.That(await adapter.RegisterAsync(duplicate, viewer, default), Is.EqualTo(AssetRegistrationOutcome.Forbidden));
        Assert.That(await adapter.RegisterAsync(duplicate, owner, default), Is.EqualTo(AssetRegistrationOutcome.IdentityConflict));
        second.Archive(owner);
        memory.SeedVault(second);
        Assert.That(await adapter.RegisterAsync(duplicate, owner, default), Is.EqualTo(AssetRegistrationOutcome.VaultArchived));
        Assert.That(adapter.Inspect(original.Id), Is.SameAs(snapshot));
    }

    [Test]
    public async Task MutatingCallerAfterInsertionDoesNotChangeSnapshot()
    {
        var owner = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, owner).Vault!;
        var memory = new InMemoryHomeVaultStore();
        memory.SeedVault(vault);
        var adapter = new InMemoryAssetRegistrationStore(memory);
        var asset = Asset.Create(Guid.NewGuid(), vault.Id, "Example").Asset!;
        await adapter.RegisterAsync(asset, owner, default);
        var snapshot = adapter.Inspect(asset.Id);
        asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary);
        asset.AddEvidence(Guid.NewGuid(), "Note", EvidenceKind.Note, "Example");
        Assert.That(adapter.Inspect(asset.Id), Is.SameAs(snapshot));
        Assert.That(adapter.Inspect(asset.Id)!.Name, Is.EqualTo("Example"));
    }

    [Test]
    public async Task ConcurrentInsertionsAcrossAdaptersHaveOneWinner()
    {
        var memory = new InMemoryHomeVaultStore();
        var owner = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, owner).Vault!;
        memory.SeedVault(vault);
        var id = Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            await start.Task;
            return await new InMemoryAssetRegistrationStore(memory).RegisterAsync(Asset.Create(id, vault.Id, $"Example {index}").Asset!, owner, default);
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(operations);
        Assert.That(results.Count(outcome => outcome == AssetRegistrationOutcome.Added), Is.EqualTo(1));
        Assert.That(results.Count(outcome => outcome == AssetRegistrationOutcome.IdentityConflict), Is.EqualTo(31));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void RejectsUnboundOrPrepopulatedAssets(int kind)
    {
        var memory = new InMemoryHomeVaultStore();
        var adapter = new InMemoryAssetRegistrationStore(memory);
        var asset = kind == 0 ? Asset.Create(Guid.NewGuid(), "Example").Asset! : Asset.Create(Guid.NewGuid(), Guid.NewGuid(), "Example").Asset!;
        if (kind == 1) asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary);
        if (kind == 2) asset.AddEvidence(Guid.NewGuid(), "Note", EvidenceKind.Note, "Example");
        Assert.ThrowsAsync<ArgumentException>(async () => await adapter.RegisterAsync(asset, Guid.NewGuid(), default));
        Assert.That(adapter.Inspect(asset.Id), Is.Null);
    }

    [Test]
    public async Task CancellationAndProgrammingErrorsDoNotWrite()
    {
        var memory = new InMemoryHomeVaultStore();
        var owner = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, owner).Vault!;
        memory.SeedVault(vault);
        var adapter = new InMemoryAssetRegistrationStore(memory);
        var asset = Asset.Create(Guid.NewGuid(), vault.Id, "Example").Asset!;
        Assert.Throws<ArgumentNullException>(() => new InMemoryAssetRegistrationStore(null!));
        Assert.Throws<ArgumentNullException>(() => new InMemoryVaultRepository(null!));
        Assert.ThrowsAsync<ArgumentNullException>(async () => await adapter.RegisterAsync(null!, owner, default));
        Assert.ThrowsAsync<ArgumentException>(async () => await adapter.RegisterAsync(asset, Guid.Empty, default));
        Assert.ThrowsAsync<OperationCanceledException>(async () => await adapter.RegisterAsync(asset, owner, new CancellationToken(true)));
        Assert.That(adapter.Inspect(asset.Id), Is.Null);
        Assert.That(await adapter.RegisterAsync(asset, owner, default), Is.EqualTo(AssetRegistrationOutcome.Added));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WaitingRegistrationRechecksStateAndCancellationAtInsertion(bool cancel)
    {
        var memory = new InMemoryHomeVaultStore();
        var owner = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, owner).Vault!;
        memory.SeedVault(vault);
        var adapter = new InMemoryAssetRegistrationStore(memory);
        var asset = Asset.Create(Guid.NewGuid(), vault.Id, "Example").Asset!;
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        Task<AssetRegistrationOutcome> pending;
        lock (memory.Gate)
        {
            pending = Task.Run(() =>
            {
                started.Set();
                return adapter.RegisterAsync(asset, owner, cancellation.Token);
            });
            Assert.That(started.Wait(TimeSpan.FromSeconds(5)), Is.True);
            if (cancel)
                cancellation.Cancel();
            else
            {
                vault.Archive(owner);
                memory.SeedVault(vault);
            }
        }
        if (cancel)
            Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        else
            Assert.That(await pending, Is.EqualTo(AssetRegistrationOutcome.VaultArchived));
        Assert.That(adapter.Inspect(asset.Id), Is.Null);
    }

    private sealed class Actor(Guid id) : ICurrentActor
    {
        public Guid? ActorId => id;
    }
}
