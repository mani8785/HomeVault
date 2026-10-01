using HomeVault.Application.Assets;
using HomeVault.Application.Identity;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class InspectAssetUseCaseTests
{
    [Test]
    public async Task ReadsActorPerCallAndPassesIdentityAndCancellation()
    {
        var actor = new Actor { Id = Guid.NewGuid() };
        var store = new Store();
        var useCase = new InspectAssetUseCase(actor, store);
        var id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var view = await useCase.ExecuteAsync(id, cancellation.Token);
        Assert.That(view!.Id, Is.EqualTo(id));
        Assert.That(store.ActorId, Is.EqualTo(actor.Id));
        Assert.That(store.Token, Is.EqualTo(cancellation.Token));
        actor.Id = Guid.NewGuid();
        await useCase.ExecuteAsync(id);
        Assert.That(store.ActorId, Is.EqualTo(actor.Id));
        actor.Id = null;
        Assert.That(await useCase.ExecuteAsync(id), Is.Null);
        actor.Id = Guid.Empty;
        Assert.That(await useCase.ExecuteAsync(id), Is.Null);
        actor.Id = Guid.NewGuid();
        Assert.That(await useCase.ExecuteAsync(Guid.Empty), Is.Null);
        Assert.That(store.Calls, Is.EqualTo(2));
        await Assert.ThrowsAsync<OperationCanceledException>(() => useCase.ExecuteAsync(id, new CancellationToken(true)));
        Assert.That(actor.Reads, Is.EqualTo(5));
    }

    private sealed class Actor : ICurrentActor
    {
        public Guid? Id { get; set; }
        public int Reads { get; private set; }
        public Guid? ActorId { get { Reads++; return Id; } }
    }
    private sealed class Store : IAssetInspectionStore
    {
        public Guid ActorId { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<InspectedAsset?> FindAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken)
        {
            Calls++;
            ActorId = actorId;
            Token = cancellationToken;
            return Task.FromResult<InspectedAsset?>(new InspectedAsset(assetId, Guid.NewGuid(), "Example"));
        }
    }
}
