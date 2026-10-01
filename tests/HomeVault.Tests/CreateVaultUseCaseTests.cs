using HomeVault.Application.Identity;
using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class CreateVaultUseCaseTests
{
    [TestCase(VaultType.Personal)]
    [TestCase(VaultType.Household)]
    [TestCase(VaultType.Organization)]
    public async Task CreatesVaultForCurrentActorAndPreservesMetadata(VaultType type)
    {
        var actor = new TestActor { Identity = Guid.NewGuid() };
        var useCase = new CreateVaultUseCase(actor, new TestRepository());
        var id = Guid.NewGuid();
        var result = await useCase.ExecuteAsync(new CreateVaultRequest(id, "  Example – København  ", type));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Error, Is.EqualTo(CreateVaultError.None));
        Assert.That(result.Vault!.Id, Is.EqualTo(id));
        Assert.That(result.Vault.Name, Is.EqualTo("  Example – København  "));
        Assert.That(result.Vault.Type, Is.EqualTo(type));
        Assert.That(result.Vault.Status, Is.EqualTo(VaultStatus.Active));
        Assert.That(result.Vault.InitialOwnerId, Is.EqualTo(actor.Identity));
        Assert.That(actor.Reads, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingIdentityPrecedesInvalidRequestFields(bool empty)
    {
        var actor = new TestActor { Identity = empty ? Guid.Empty : null };
        var repository = new TestRepository();
        var result = await new CreateVaultUseCase(actor, repository).ExecuteAsync(new CreateVaultRequest(Guid.Empty, null, (VaultType)99));
        Assert.That(repository.Calls, Is.Zero);
        AssertFailure(result, CreateVaultError.Unauthenticated);
        Assert.That(actor.Reads, Is.EqualTo(1));
    }

    [Test]
    public async Task EmptyVaultIdentityPrecedesOtherDomainFailures()
    {
        var repository = new TestRepository();
        var useCase = new CreateVaultUseCase(new TestActor { Identity = Guid.NewGuid() }, repository);
        AssertFailure(await useCase.ExecuteAsync(new CreateVaultRequest(Guid.Empty, null, (VaultType)99)), CreateVaultError.EmptyIdentity);
        Assert.That(repository.Calls, Is.Zero);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    [TestCase("\u2003")]
    public async Task InvalidNamePrecedesInvalidType(string? name)
    {
        var repository = new TestRepository();
        var useCase = new CreateVaultUseCase(new TestActor { Identity = Guid.NewGuid() }, repository);
        AssertFailure(await useCase.ExecuteAsync(new CreateVaultRequest(Guid.NewGuid(), name, (VaultType)99)), CreateVaultError.BlankName);
        Assert.That(repository.Calls, Is.Zero);
    }

    [TestCase(-1)]
    [TestCase(3)]
    public async Task InvalidTypeMapsToApplicationError(int type)
    {
        var repository = new TestRepository();
        var useCase = new CreateVaultUseCase(new TestActor { Identity = Guid.NewGuid() }, repository);
        AssertFailure(await useCase.ExecuteAsync(new CreateVaultRequest(Guid.NewGuid(), "Example", (VaultType)type)), CreateVaultError.InvalidType);
        Assert.That(repository.Calls, Is.Zero);
    }

    [Test]
    public async Task IdentityIsReadOncePerExecutionAndNeverCachedAcrossCalls()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var actor = new TestActor { Identity = firstId };
        var useCase = new CreateVaultUseCase(actor, new TestRepository());
        var request = new CreateVaultRequest(Guid.NewGuid(), "Example", VaultType.Personal);
        var first = await useCase.ExecuteAsync(request);
        actor.Identity = secondId;
        var second = await useCase.ExecuteAsync(new CreateVaultRequest(Guid.NewGuid(), "Example", VaultType.Personal));
        actor.Identity = null;
        AssertFailure(await useCase.ExecuteAsync(request), CreateVaultError.Unauthenticated);
        Assert.That(first.Vault!.InitialOwnerId, Is.EqualTo(firstId));
        Assert.That(second.Vault!.InitialOwnerId, Is.EqualTo(secondId));
        Assert.That(actor.Reads, Is.EqualTo(3));
    }

    [Test]
    public async Task NullProgrammingInputsAreRejectedExplicitly()
    {
        Assert.Throws<ArgumentNullException>(() => new CreateVaultUseCase(null!, new TestRepository()));
        var actor = new TestActor();
        Assert.Throws<ArgumentNullException>(() => new CreateVaultUseCase(actor, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new CreateVaultUseCase(actor, new TestRepository()).ExecuteAsync(null!));
        Assert.That(actor.Reads, Is.Zero);
    }

    [Test]
    public async Task DiagnosticsDoNotFormatRequestOrResultMetadata()
    {
        var request = new CreateVaultRequest(Guid.NewGuid(), "Private fictional label", VaultType.Personal);
        var result = await new CreateVaultUseCase(new TestActor { Identity = Guid.NewGuid() }, new TestRepository()).ExecuteAsync(request);
        Assert.That(request.ToString(), Is.EqualTo("CreateVaultRequest"));
        Assert.That(result.ToString(), Is.EqualTo("CreateVaultResult"));
        Assert.That(result.Vault!.ToString(), Is.EqualTo("CreatedVault"));
    }

    private static void AssertFailure(CreateVaultResult result, CreateVaultError error)
    {
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Vault, Is.Null);
        Assert.That(result.Error, Is.EqualTo(error));
    }

    private sealed class TestRepository : IVaultRepository
    {
        public int Calls { get; private set; }
        public Vault? Received { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource<VaultAddOutcome>? Completion { get; init; }
        public Task<VaultAddOutcome> AddAsync(Vault vault, CancellationToken cancellationToken)
        {
            Calls++;
            Received = vault;
            Token = cancellationToken;
            return Completion?.Task ?? Task.FromResult(VaultAddOutcome.Added);
        }
    }

    [Test]
    public async Task SuccessWaitsForStorageAndPassesOwnerAndCancellation()
    {
        var completion = new TaskCompletionSource<VaultAddOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new TestRepository { Completion = completion };
        var actor = new TestActor { Identity = Guid.NewGuid() };
        using var cancellation = new CancellationTokenSource();
        var request = new CreateVaultRequest(Guid.NewGuid(), "Example", VaultType.Household);
        var pending = new CreateVaultUseCase(actor, repository).ExecuteAsync(request, cancellation.Token);
        Assert.That(pending.IsCompleted, Is.False);
        Assert.That(repository.Calls, Is.EqualTo(1));
        Assert.That(repository.Received!.Id, Is.EqualTo(request.Id));
        Assert.That(repository.Received.Memberships.Single().ActorId, Is.EqualTo(actor.Identity));
        Assert.That(repository.Token, Is.EqualTo(cancellation.Token));
        completion.SetResult(VaultAddOutcome.Added);
        Assert.That((await pending).IsSuccess, Is.True);
    }

    [Test]
    public async Task ConflictDoesNotReturnCreatedView()
    {
        var completion = new TaskCompletionSource<VaultAddOutcome>();
        completion.SetResult(VaultAddOutcome.IdentityConflict);
        var repository = new TestRepository { Completion = completion };
        var result = await new CreateVaultUseCase(new TestActor { Identity = Guid.NewGuid() }, repository)
            .ExecuteAsync(new CreateVaultRequest(Guid.NewGuid(), "Example", VaultType.Personal));
        AssertFailure(result, CreateVaultError.IdentityConflict);
        Assert.That(repository.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task PreCancellationDoesNotReadActorOrCallStorage()
    {
        var actor = new TestActor { Identity = Guid.NewGuid() };
        var repository = new TestRepository();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new CreateVaultUseCase(actor, repository)
            .ExecuteAsync(new CreateVaultRequest(Guid.NewGuid(), "Example", VaultType.Personal), new CancellationToken(true)));
        Assert.That(actor.Reads, Is.Zero);
        Assert.That(repository.Calls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StorageFailurePropagatesWithoutRetry(bool cancelled)
    {
        var completion = new TaskCompletionSource<VaultAddOutcome>();
        var failure = cancelled ? (Exception)new OperationCanceledException() : new IOException("Storage unavailable.");
        completion.SetException(failure);
        var repository = new TestRepository { Completion = completion };
        var caught = await Assert.CatchAsync<Exception>(() => new CreateVaultUseCase(new TestActor { Identity = Guid.NewGuid() }, repository)
            .ExecuteAsync(new CreateVaultRequest(Guid.NewGuid(), "Example", VaultType.Personal)));
        Assert.That(caught, Is.SameAs(failure));
        Assert.That(repository.Calls, Is.EqualTo(1));
    }

    private sealed class TestActor : ICurrentActor
    {
        public Guid? Identity { get; set; }
        public int Reads { get; private set; }
        public Guid? ActorId
        {
            get
            {
                Reads++;
                return Identity;
            }
        }
    }
}
