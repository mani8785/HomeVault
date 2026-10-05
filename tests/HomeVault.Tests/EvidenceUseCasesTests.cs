using HomeVault.Application.Assets;
using HomeVault.Application.Identity;
using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

public sealed class EvidenceUseCasesTests
{
    [TestCase("add")]
    [TestCase("remove")]
    [TestCase("list")]
    [TestCase("content")]
    public async Task TrustedActorReadOnceCancellationAndInvalidIdentitiesDoNotAccessStore(string operation)
    {
        var actor = new Actor(); var store = new Store();
        var useCases = new EvidenceUseCases(actor, store);
        var asset = Guid.NewGuid(); var id = Guid.NewGuid();
        async Task Call(CancellationToken token = default)
        {
            switch (operation)
            {
                case "add": await useCases.AddAsync(asset, id, " Label ", EvidenceKind.Url, "https://example.invalid/?token=fictional", token); break;
                case "remove": await useCases.RemoveAsync(asset, id, token); break;
                case "list": await useCases.ListAsync(asset, token); break;
                default: await useCases.ReadContentAsync(asset, id, token); break;
            }
        }
        await Call();
        Assert.That(store.Calls, Is.Zero);
        actor.Id = Guid.NewGuid(); asset = Guid.Empty;
        await Call();
        Assert.That(store.Calls, Is.Zero);
        asset = Guid.NewGuid();
        using var source = new CancellationTokenSource();
        await Call(source.Token);
        Assert.That(actor.Reads, Is.EqualTo(3));
        Assert.That(store.Calls, Is.EqualTo(1));
        Assert.That(store.ActorId, Is.EqualTo(actor.Id));
        Assert.That(store.Token, Is.EqualTo(source.Token));
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Call(source.Token));
        Assert.That(actor.Reads, Is.EqualTo(3));
    }

    [Test]
    public async Task MutationPropagatesSafeOutcomesAndRejectsUnexpectedStoreOutcome()
    {
        var store = new Store();
        var useCases = new EvidenceUseCases(new Actor { Id = Guid.NewGuid() }, store);
        Assert.That(await useCases.AddAsync(Guid.NewGuid(), Guid.NewGuid(), " Label ", EvidenceKind.Note, " text "), Is.EqualTo(EvidenceOutcome.Forbidden));
        Assert.That(store.Label, Is.EqualTo(" Label "));
        Assert.That(store.Content, Is.EqualTo(" text "));
        store.Outcome = (EvidenceOutcome)99;
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCases.RemoveAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    private sealed class Actor : ICurrentActor
    {
        internal Guid? Id;
        internal int Reads;
        public Guid? ActorId { get { Reads++; return Id; } }
    }
    private sealed class Store : IEvidenceStore
    {
        internal int Calls;
        internal Guid ActorId;
        internal CancellationToken Token;
        internal string? Label, Content;
        internal EvidenceOutcome Outcome = EvidenceOutcome.Forbidden;
        private void Capture(Guid actor, CancellationToken token) { Calls++; ActorId = actor; Token = token; }
        public Task<EvidenceOutcome> AddAsync(Guid assetId, Guid actorId, Guid id, string? label, EvidenceKind kind, string? content, CancellationToken cancellationToken)
        {
            Capture(actorId, cancellationToken); Label = label; Content = content;
            return Task.FromResult(Outcome);
        }
        public Task<EvidenceOutcome> RemoveAsync(Guid assetId, Guid actorId, Guid id, CancellationToken cancellationToken)
        {
            Capture(actorId, cancellationToken); return Task.FromResult(Outcome);
        }
        public Task<IReadOnlyList<EvidenceMetadata>?> ListAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken)
        {
            Capture(actorId, cancellationToken); return Task.FromResult<IReadOnlyList<EvidenceMetadata>?>(Array.Empty<EvidenceMetadata>());
        }
        public Task<EvidenceContent?> ReadContentAsync(Guid assetId, Guid actorId, Guid id, CancellationToken cancellationToken)
        {
            Capture(actorId, cancellationToken); return Task.FromResult<EvidenceContent?>(null);
        }
    }
}
