using HomeVault.Application.Identity;
using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class VaultMembershipUseCasesTests
{
    [TestCase("add")]
    [TestCase("change")]
    [TestCase("remove")]
    public async Task ValidatesBeforeStorageAndReadsActorOnce(string operation)
    {
        var actor = new Actor();
        var store = new Store();
        var useCases = new VaultMembershipUseCases(actor, store);
        Assert.That(await Call(useCases, operation, Guid.Empty, Guid.Empty), Is.EqualTo(MembershipOutcome.Unauthenticated));
        actor.Id = Guid.Empty;
        Assert.That(await Call(useCases, operation, Guid.NewGuid(), Guid.NewGuid()), Is.EqualTo(MembershipOutcome.Unauthenticated));
        actor.Id = Guid.NewGuid();
        Assert.That(await Call(useCases, operation, Guid.Empty, Guid.NewGuid()), Is.EqualTo(MembershipOutcome.InvalidIdentity));
        Assert.That(await Call(useCases, operation, Guid.NewGuid(), Guid.Empty), Is.EqualTo(MembershipOutcome.InvalidIdentity));
        await Assert.ThrowsAsync<OperationCanceledException>(() => Call(useCases, operation, Guid.NewGuid(), Guid.NewGuid(), new CancellationToken(true)));
        Assert.That(actor.Reads, Is.EqualTo(4));
        Assert.That(store.Calls, Is.Zero);
    }

    [TestCase("add")]
    [TestCase("change")]
    [TestCase("remove")]
    public async Task AwaitsStorageAndPassesTrustedIdentityAndCancellation(string operation)
    {
        var actor = new Actor { Id = Guid.NewGuid() };
        var store = new Store();
        var vault = Guid.NewGuid();
        var target = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var pending = Call(new VaultMembershipUseCases(actor, store), operation, vault, target, cancellation.Token);
        Assert.That(pending.IsCompleted, Is.False);
        Assert.That(store.Received, Is.EqualTo((operation, vault, actor.Id.Value, target, cancellation.Token)));
        store.Completion.SetResult(MembershipOutcome.Succeeded);
        Assert.That(await pending, Is.EqualTo(MembershipOutcome.Succeeded));
        Assert.That(actor.Reads, Is.EqualTo(1));
    }

    [TestCase(MembershipOutcome.Unavailable)]
    [TestCase(MembershipOutcome.Forbidden)]
    [TestCase(MembershipOutcome.Archived)]
    [TestCase(MembershipOutcome.DuplicateMember)]
    [TestCase(MembershipOutcome.LastOwner)]
    public async Task PreservesSafeStorageFailures(MembershipOutcome outcome)
    {
        var store = new Store();
        store.Completion.SetResult(outcome);
        Assert.That(await Call(new VaultMembershipUseCases(new Actor { Id = Guid.NewGuid() }, store), "add", Guid.NewGuid(), Guid.NewGuid()), Is.EqualTo(outcome));
    }

    [Test]
    public async Task RejectsInvalidRolesAndCollaboratorsAndDoesNotRetryFailure()
    {
        var actor = new Actor { Id = Guid.NewGuid() };
        var store = new Store();
        var useCases = new VaultMembershipUseCases(actor, store);
        Assert.That(await useCases.AddAsync(Guid.NewGuid(), Guid.NewGuid(), (VaultRole)99), Is.EqualTo(MembershipOutcome.InvalidRole));
        Assert.That(await useCases.ChangeRoleAsync(Guid.NewGuid(), Guid.NewGuid(), (VaultRole)(-1)), Is.EqualTo(MembershipOutcome.InvalidRole));
        Assert.That(store.Calls, Is.Zero);
        var failure = new IOException("Failure");
        store.Completion.SetException(failure);
        Assert.That(await Assert.ThrowsAsync<IOException>(() => Call(useCases, "remove", Guid.NewGuid(), Guid.NewGuid())), Is.SameAs(failure));
        Assert.That(store.Calls, Is.EqualTo(1));
        Assert.Throws<ArgumentNullException>(() => new VaultMembershipUseCases(null!, store));
        Assert.Throws<ArgumentNullException>(() => new VaultMembershipUseCases(actor, null!));
        store = new Store();
        store.Completion.SetResult((MembershipOutcome)99);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Call(new VaultMembershipUseCases(actor, store), "add", Guid.NewGuid(), Guid.NewGuid()));
    }

    private static Task<MembershipOutcome> Call(VaultMembershipUseCases useCases, string operation, Guid vault, Guid target, CancellationToken token = default) => operation switch
    {
        "add" => useCases.AddAsync(vault, target, VaultRole.Viewer, token),
        "change" => useCases.ChangeRoleAsync(vault, target, VaultRole.Viewer, token),
        _ => useCases.RemoveAsync(vault, target, token)
    };

    private sealed class Actor : ICurrentActor
    {
        internal Guid? Id;
        internal int Reads;
        public Guid? ActorId { get { Reads++; return Id; } }
    }

    private sealed class Store : IVaultMembershipStore
    {
        internal int Calls;
        internal (string, Guid, Guid, Guid, CancellationToken) Received;
        internal readonly TaskCompletionSource<MembershipOutcome> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<MembershipOutcome> Capture(string operation, Guid vault, Guid actor, Guid target, CancellationToken token)
        {
            Calls++;
            Received = (operation, vault, actor, target, token);
            return Completion.Task;
        }
        public Task<MembershipOutcome> AddAsync(Guid vaultId, Guid actorId, Guid targetActorId, VaultRole role, CancellationToken cancellationToken) => Capture("add", vaultId, actorId, targetActorId, cancellationToken);
        public Task<MembershipOutcome> ChangeRoleAsync(Guid vaultId, Guid actorId, Guid targetActorId, VaultRole role, CancellationToken cancellationToken) => Capture("change", vaultId, actorId, targetActorId, cancellationToken);
        public Task<MembershipOutcome> RemoveAsync(Guid vaultId, Guid actorId, Guid targetActorId, CancellationToken cancellationToken) => Capture("remove", vaultId, actorId, targetActorId, cancellationToken);
    }
}
