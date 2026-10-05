using HomeVault.Application.Identity;
using HomeVault.Application.Reminders;
using NUnit.Framework;

namespace HomeVault.Tests;

public sealed class ReminderUseCasesTests
{
    [TestCase("create")]
    [TestCase("update")]
    [TestCase("complete")]
    [TestCase("cancel")]
    [TestCase("read")]
    public async Task IdentityAndCancellationAreCheckedBeforeStorage(string operation)
    {
        var actor = new Actor(); var store = new Store(); var useCases = new ReminderUseCases(actor, store);
        var vault = Guid.NewGuid(); var id = Guid.NewGuid();
        async Task Call(CancellationToken token = default)
        {
            switch (operation)
            {
                case "create": await useCases.CreateAsync(vault, id, Guid.NewGuid(), null, default, token); break;
                case "update": await useCases.UpdateAsync(vault, id, null, default, token); break;
                case "complete": await useCases.CompleteAsync(vault, id, token); break;
                case "cancel": await useCases.CancelAsync(vault, id, token); break;
                default: await useCases.FindAsync(vault, id, token); break;
            }
        }
        await Call(); Assert.That(store.Calls, Is.Zero);
        actor.Id = Guid.NewGuid(); vault = Guid.Empty;
        await Call(); Assert.That(store.Calls, Is.Zero);
        vault = Guid.NewGuid(); id = Guid.Empty;
        await Call(); Assert.That(store.Calls, Is.Zero);
        id = Guid.NewGuid(); using var cancel = new CancellationTokenSource();
        await Call(cancel.Token);
        Assert.That(actor.Reads, Is.EqualTo(4)); Assert.That(store.Calls, Is.EqualTo(1));
        Assert.That(store.Actor, Is.EqualTo(actor.Id)); Assert.That(store.Token, Is.EqualTo(cancel.Token));
        cancel.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => Call(cancel.Token));
        Assert.That(actor.Reads, Is.EqualTo(4));
    }
    [Test]
    public async Task EmptyAssetDoesNotReachStorageAndUnknownOutcomeFailsClosed()
    {
        var store = new Store(); var cases = new ReminderUseCases(new Actor { Id = Guid.NewGuid() }, store);
        Assert.That(await cases.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, null, default), Is.EqualTo(ReminderOutcome.InvalidIdentity));
        Assert.That(store.Calls, Is.Zero);
        store.Outcome = (ReminderOutcome)99;
        await Assert.ThrowsAsync<InvalidOperationException>(() => cases.CompleteAsync(Guid.NewGuid(), Guid.NewGuid()));
    }
    private sealed class Actor : ICurrentActor
    {
        internal Guid? Id; internal int Reads;
        public Guid? ActorId { get { Reads++; return Id; } }
    }
    private sealed class Store : IReminderStore
    {
        internal int Calls; internal Guid Actor; internal CancellationToken Token;
        internal ReminderOutcome Outcome = ReminderOutcome.Forbidden;
        private Task<ReminderOutcome> Capture(Guid actor, CancellationToken token) { Calls++; Actor = actor; Token = token; return Task.FromResult(Outcome); }
        public Task<ReminderOutcome> CreateAsync(Guid vaultId, Guid actorId, Guid id, Guid assetId, string? action, DateTimeOffset dueAt, CancellationToken token) => Capture(actorId, token);
        public Task<ReminderOutcome> UpdateAsync(Guid vaultId, Guid actorId, Guid id, string? action, DateTimeOffset dueAt, CancellationToken token) => Capture(actorId, token);
        public Task<ReminderOutcome> CompleteAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token) => Capture(actorId, token);
        public Task<ReminderOutcome> CancelAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token) => Capture(actorId, token);
        public Task<ReminderSnapshot?> FindAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token) { Capture(actorId, token); return Task.FromResult<ReminderSnapshot?>(null); }
    }
}
