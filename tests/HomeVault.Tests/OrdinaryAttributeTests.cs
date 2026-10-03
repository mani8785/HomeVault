using HomeVault.Application.Assets;
using HomeVault.Application.Identity;
using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

public sealed class OrdinaryAttributeTests
{
    [Test]
    public void RestorationPreservesIdentitySpellingAndImmutableSnapshots()
    {
        var id = Guid.NewGuid();
        var vault = Guid.NewGuid();
        var asset = Asset.RestoreOrdinaryAttributes(id, vault, " Item ", new[] { KeyValuePair.Create("Étage/طبقه", "  first  ") });
        var snapshot = asset.Attributes;
        Assert.That(asset.Id, Is.EqualTo(id));
        Assert.That(asset.VaultId, Is.EqualTo(vault));
        Assert.That(asset.Name, Is.EqualTo(" Item "));
        Assert.That(asset.ChangeAttribute(" étage/طبقه ", "second"), Is.EqualTo(AssetAttributeError.None));
        Assert.That(snapshot.Single().Value, Is.EqualTo("  first  "));
        Assert.That(asset.Attributes.Single().Name, Is.EqualTo("Étage/طبقه"));
        Assert.That(asset.RemoveAttribute("ÉTAGE/طبقه"), Is.EqualTo(AssetAttributeError.None));
        Assert.That(snapshot.Single().Value, Is.EqualTo("  first  "));
    }

    [TestCase(" ", "value")]
    [TestCase(" key ", "value")]
    [TestCase("key", " ")]
    public void RestorationRejectsInvalidStoredEntriesWithoutEchoingValues(string name, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Asset.RestoreOrdinaryAttributes(Guid.NewGuid(), Guid.NewGuid(), "Private sentinel",
            new[] { KeyValuePair.Create(name, value) }));
        Assert.That(exception!.Message, Is.EqualTo("Invalid stored Asset state."));
    }

    [Test]
    public void RestorationRejectsDuplicatesAndInvalidIdentity()
    {
        Assert.Throws<InvalidOperationException>(() => Asset.RestoreOrdinaryAttributes(Guid.NewGuid(), Guid.NewGuid(), "Item",
            new[] { KeyValuePair.Create("É", "first"), KeyValuePair.Create("é", "second") }));
        Assert.Throws<InvalidOperationException>(() => Asset.RestoreOrdinaryAttributes(Guid.Empty, Guid.NewGuid(), "Item", []));
        Assert.Throws<InvalidOperationException>(() => Asset.RestoreOrdinaryAttributes(Guid.NewGuid(), Guid.Empty, "Item", []));
        Assert.Throws<InvalidOperationException>(() => Asset.RestoreOrdinaryAttributes(Guid.NewGuid(), Guid.NewGuid(), " ", []));
    }

    [TestCase("add")]
    [TestCase("change")]
    [TestCase("remove")]
    public async Task UseCasesReadTrustedActorOnceAndPreserveArgumentsAndCancellation(string operation)
    {
        var actor = new Actor();
        var store = new Store();
        var useCases = new OrdinaryAttributeUseCases(actor, store);
        var id = Guid.NewGuid();
        Task<AttributeOutcome> Call(CancellationToken token = default) => operation switch
        {
            "add" => useCases.AddAsync(id, " Name ", " value ", AttributeSensitivity.Sensitive, token),
            "change" => useCases.ChangeAsync(id, " Name ", " value ", token),
            _ => useCases.RemoveAsync(id, " Name ", token)
        };
        Assert.That(await Call(), Is.EqualTo(AttributeOutcome.Unauthenticated));
        Assert.That(store.Calls, Is.Zero);
        actor.Id = Guid.NewGuid();
        id = Guid.Empty;
        Assert.That(await Call(), Is.EqualTo(AttributeOutcome.InvalidIdentity));
        Assert.That(store.Calls, Is.Zero);
        id = Guid.NewGuid();
        using var source = new CancellationTokenSource();
        Assert.That(await Call(source.Token), Is.EqualTo(AttributeOutcome.Forbidden));
        Assert.That(actor.Reads, Is.EqualTo(3));
        Assert.That(store.ActorId, Is.EqualTo(actor.Id));
        Assert.That(store.Name, Is.EqualTo(" Name "));
        Assert.That(store.Token, Is.EqualTo(source.Token));
        if (operation == "add") Assert.That(store.Sensitivity, Is.EqualTo(AttributeSensitivity.Sensitive));
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Call(source.Token));
        Assert.That(actor.Reads, Is.EqualTo(3));
    }

    [Test]
    public async Task ReadRefusesSensitiveStoreOutputAndUnauthenticatedAccess()
    {
        var actor = new Actor();
        var store = new Store();
        var useCases = new OrdinaryAttributeUseCases(actor, store);
        Assert.That(await useCases.ListAsync(Guid.NewGuid()), Is.Null);
        Assert.That(store.Calls, Is.Zero);
        actor.Id = Guid.NewGuid();
        var asset = Asset.Create(Guid.NewGuid(), "Item").Asset!;
        asset.AddAttribute("Private sentinel", "Private sentinel", AttributeSensitivity.Sensitive);
        store.Entries = asset.Attributes;
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCases.ListAsync(asset.Id));
        Assert.That(actor.Reads, Is.EqualTo(2));
    }

    private sealed class Actor : ICurrentActor
    {
        internal Guid? Id;
        internal int Reads;
        public Guid? ActorId { get { Reads++; return Id; } }
    }

    private sealed class Store : IOrdinaryAttributeStore
    {
        internal int Calls;
        internal Guid ActorId;
        internal string? Name;
        internal CancellationToken Token;
        internal AttributeSensitivity Sensitivity;
        internal IReadOnlyList<AssetAttribute>? Entries;
        public Task<AttributeOutcome> AddAsync(Guid assetId, Guid actorId, string? name, string? value, AttributeSensitivity sensitivity, CancellationToken cancellationToken)
        {
            Calls++; ActorId = actorId; Name = name; Token = cancellationToken; Sensitivity = sensitivity;
            return Task.FromResult(AttributeOutcome.Forbidden);
        }
        public Task<AttributeOutcome> ChangeAsync(Guid assetId, Guid actorId, string? name, string? value, CancellationToken cancellationToken) =>
            AddAsync(assetId, actorId, name, value, AttributeSensitivity.Ordinary, cancellationToken);
        public Task<AttributeOutcome> RemoveAsync(Guid assetId, Guid actorId, string? name, CancellationToken cancellationToken) =>
            AddAsync(assetId, actorId, name, null, AttributeSensitivity.Ordinary, cancellationToken);
        public Task<IReadOnlyList<AssetAttribute>?> ListAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Entries);
        }
    }
}
