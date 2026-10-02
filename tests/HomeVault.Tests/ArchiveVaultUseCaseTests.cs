using HomeVault.Application.Identity;
using HomeVault.Application.Vaults;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class ArchiveVaultUseCaseTests
{
    [Test]
    public async Task ValidatesBeforeStorageAndReadsCurrentActorOncePerCall()
    {
        var actor = new Actor();
        var store = new Store();
        var useCase = new ArchiveVaultUseCase(actor, store);
        Assert.That(await useCase.ExecuteAsync(Guid.Empty), Is.EqualTo(ArchiveVaultOutcome.Unauthenticated));
        actor.Id = Guid.Empty;
        Assert.That(await useCase.ExecuteAsync(Guid.NewGuid()), Is.EqualTo(ArchiveVaultOutcome.Unauthenticated));
        actor.Id = Guid.NewGuid();
        Assert.That(await useCase.ExecuteAsync(Guid.Empty), Is.EqualTo(ArchiveVaultOutcome.InvalidIdentity));
        Assert.That(store.Calls, Is.Zero);
        Assert.That(actor.Reads, Is.EqualTo(3));
        await Assert.ThrowsAsync<OperationCanceledException>(() => useCase.ExecuteAsync(Guid.NewGuid(), new CancellationToken(true)));
        Assert.That(actor.Reads, Is.EqualTo(3));
    }

    [TestCase(ArchiveVaultOutcome.Archived)]
    [TestCase(ArchiveVaultOutcome.Unavailable)]
    [TestCase(ArchiveVaultOutcome.Forbidden)]
    public async Task AwaitsCommitAndPassesTrustedIdentityAndCancellation(ArchiveVaultOutcome outcome)
    {
        var actor = new Actor { Id = Guid.NewGuid() };
        var store = new Store();
        var vault = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var pending = new ArchiveVaultUseCase(actor, store).ExecuteAsync(vault, cancellation.Token);
        Assert.That(pending.IsCompleted, Is.False);
        Assert.That(store.Received, Is.EqualTo((vault, actor.Id.Value, cancellation.Token)));
        store.Completion.SetResult(outcome);
        Assert.That(await pending, Is.EqualTo(outcome));
        Assert.That(actor.Reads, Is.EqualTo(1));
    }

    [Test]
    public async Task PropagatesFailureWithoutRetryAndRejectsUnknownOutcomes()
    {
        var actor = new Actor { Id = Guid.NewGuid() };
        var store = new Store();
        var failure = new IOException("Storage unavailable.");
        store.Completion.SetException(failure);
        Assert.That(await Assert.ThrowsAsync<IOException>(() => new ArchiveVaultUseCase(actor, store).ExecuteAsync(Guid.NewGuid())), Is.SameAs(failure));
        Assert.That(store.Calls, Is.EqualTo(1));
        store = new Store();
        store.Completion.SetResult((ArchiveVaultOutcome)99);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ArchiveVaultUseCase(actor, store).ExecuteAsync(Guid.NewGuid()));
        Assert.Throws<ArgumentNullException>(() => new ArchiveVaultUseCase(null!, store));
        Assert.Throws<ArgumentNullException>(() => new ArchiveVaultUseCase(actor, null!));
    }

    private sealed class Actor : ICurrentActor
    {
        internal Guid? Id;
        internal int Reads;
        public Guid? ActorId { get { Reads++; return Id; } }
    }

    private sealed class Store : IVaultArchiveStore
    {
        internal int Calls;
        internal (Guid, Guid, CancellationToken) Received;
        internal readonly TaskCompletionSource<ArchiveVaultOutcome> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ArchiveVaultOutcome> ArchiveAsync(Guid vaultId, Guid actorId, CancellationToken cancellationToken)
        {
            Calls++;
            Received = (vaultId, actorId, cancellationToken);
            return Completion.Task;
        }
    }
}
