using HomeVault.Application.Identity;
using HomeVault.Application.Relationships;
using HomeVault.Domain.Relationships;
using NUnit.Framework;

namespace HomeVault.Tests;

public sealed class RelationshipUseCasesTests
{
    [TestCase("create")]
    [TestCase("remove")]
    [TestCase("read")]
    public async Task TrustedIdentityIsReadOnceAndInvalidArgumentsDoNotAccessStore(string operation)
    {
        var actor = new Actor(); var store = new Store(); var useCases = new RelationshipUseCases(actor, store);
        var vault = Guid.NewGuid(); var id = Guid.NewGuid(); var source = Guid.NewGuid(); var target = Guid.NewGuid();
        async Task Call(CancellationToken token = default)
        {
            if (operation == "create") await useCases.CreateAsync(vault, id, source, target, RelationshipKind.Covers, token);
            else if (operation == "remove") await useCases.RemoveAsync(vault, id, token);
            else await useCases.FindAsync(vault, id, token);
        }
        await Call(); Assert.That(store.Calls, Is.Zero);
        actor.Id = Guid.NewGuid(); vault = Guid.Empty;
        await Call(); Assert.That(store.Calls, Is.Zero);
        vault = Guid.NewGuid(); id = Guid.Empty;
        await Call(); Assert.That(store.Calls, Is.Zero);
        id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        await Call(cancellation.Token);
        Assert.That(actor.Reads, Is.EqualTo(4));
        Assert.That(store.Calls, Is.EqualTo(1));
        Assert.That(store.ActorId, Is.EqualTo(actor.Id));
        Assert.That(store.Token, Is.EqualTo(cancellation.Token));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Call(cancellation.Token));
        Assert.That(actor.Reads, Is.EqualTo(4));
    }
    [Test]
    public async Task EndpointValidationPrecedesStoreButKindValidationRemainsBehindAccess()
    {
        var store = new Store(); var useCases = new RelationshipUseCases(new Actor { Id = Guid.NewGuid() }, store);
        Assert.That(await useCases.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), RelationshipKind.Covers), Is.EqualTo(RelationshipOutcome.InvalidIdentity));
        Assert.That(store.Calls, Is.Zero);
        Assert.That(await useCases.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (RelationshipKind)99), Is.EqualTo(RelationshipOutcome.Forbidden));
        Assert.That(store.Kind, Is.EqualTo((RelationshipKind)99));
        store.Outcome = (RelationshipOutcome)99;
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCases.RemoveAsync(Guid.NewGuid(), Guid.NewGuid()));
    }
    private sealed class Actor : ICurrentActor
    {
        internal Guid? Id;
        internal int Reads;
        public Guid? ActorId { get { Reads++; return Id; } }
    }
    private sealed class Store : IRelationshipStore
    {
        internal int Calls;
        internal Guid ActorId;
        internal CancellationToken Token;
        internal RelationshipKind Kind;
        internal RelationshipOutcome Outcome = RelationshipOutcome.Forbidden;
        private void Capture(Guid actor, CancellationToken token) { Calls++; ActorId = actor; Token = token; }
        public Task<RelationshipOutcome> CreateAsync(Guid vaultId, Guid actorId, Guid id, Guid sourceAssetId, Guid targetAssetId, RelationshipKind kind, CancellationToken cancellationToken)
        {
            Capture(actorId, cancellationToken); Kind = kind; return Task.FromResult(Outcome);
        }
        public Task<RelationshipOutcome> RemoveAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken cancellationToken)
        {
            Capture(actorId, cancellationToken); return Task.FromResult(Outcome);
        }
        public Task<RelationshipMetadata?> FindAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken cancellationToken)
        {
            Capture(actorId, cancellationToken); return Task.FromResult<RelationshipMetadata?>(null);
        }
    }
}
