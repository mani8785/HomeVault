using HomeVault.Application.Assets;
using HomeVault.Application.Identity;
using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class RegisterAssetUseCaseTests
{
    [Test]
    public async Task AwaitsStorageAndPassesTrustedActorBindingAndCancellation()
    {
        var actor = new Actor { Id = Guid.NewGuid() };
        var completion = new TaskCompletionSource<AssetRegistrationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Store { Completion = completion };
        var request = new RegisterAssetRequest(Guid.NewGuid(), Guid.NewGuid(), "  Example  ");
        using var cancellation = new CancellationTokenSource();
        var pending = new RegisterAssetUseCase(actor, store).ExecuteAsync(request, cancellation.Token);
        Assert.That(pending.IsCompleted, Is.False);
        Assert.That(store.Asset!.Id, Is.EqualTo(request.Id));
        Assert.That(store.Asset.VaultId, Is.EqualTo(request.VaultId));
        Assert.That(store.ActorId, Is.EqualTo(actor.Id));
        Assert.That(store.Token, Is.EqualTo(cancellation.Token));
        completion.SetResult(AssetRegistrationOutcome.Added);
        var result = await pending;
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Error, Is.EqualTo(RegisterAssetError.None));
        Assert.That(result.Asset!.Id, Is.EqualTo(request.Id));
        Assert.That(result.Asset.VaultId, Is.EqualTo(request.VaultId));
        Assert.That(result.Asset.Name, Is.EqualTo(request.Name));
        Assert.That(store.Calls, Is.EqualTo(1));
        Assert.That(actor.Reads, Is.EqualTo(1));
        Assert.That(request.ToString(), Is.EqualTo("RegisterAssetRequest"));
        Assert.That(result.ToString(), Is.EqualTo("RegisterAssetResult"));
        Assert.That(result.Asset.ToString(), Is.EqualTo("RegisteredAsset"));
    }

    [TestCase(AssetRegistrationOutcome.VaultUnavailable, RegisterAssetError.VaultUnavailable)]
    [TestCase(AssetRegistrationOutcome.Forbidden, RegisterAssetError.Forbidden)]
    [TestCase(AssetRegistrationOutcome.VaultArchived, RegisterAssetError.VaultArchived)]
    [TestCase(AssetRegistrationOutcome.IdentityConflict, RegisterAssetError.IdentityConflict)]
    public async Task MapsStorageFailuresWithoutView(AssetRegistrationOutcome outcome, RegisterAssetError expected)
    {
        var store = new Store { Outcome = outcome };
        var result = await new RegisterAssetUseCase(new Actor { Id = Guid.NewGuid() }, store).ExecuteAsync(Request());
        Assert.That(result.Error, Is.EqualTo(expected));
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Asset, Is.Null);
        Assert.That(store.Calls, Is.EqualTo(1));
    }

    [TestCase(0, RegisterAssetError.Unauthenticated)]
    [TestCase(1, RegisterAssetError.EmptyIdentity)]
    [TestCase(2, RegisterAssetError.EmptyVaultIdentity)]
    [TestCase(3, RegisterAssetError.BlankName)]
    public async Task ValidationPrecedenceAvoidsStorage(int validFields, RegisterAssetError expected)
    {
        var store = new Store();
        var actor = new Actor { Id = validFields > 0 ? Guid.NewGuid() : null };
        var request = new RegisterAssetRequest(validFields > 1 ? Guid.NewGuid() : Guid.Empty,
            validFields > 2 ? Guid.NewGuid() : Guid.Empty, null);
        var result = await new RegisterAssetUseCase(actor, store).ExecuteAsync(request);
        Assert.That(result.Error, Is.EqualTo(expected));
        Assert.That(result.Asset, Is.Null);
        Assert.That(store.Calls, Is.Zero);
    }

    [Test]
    public async Task ActorIsReadOncePerCallAndNotCached()
    {
        var actor = new Actor { Id = Guid.NewGuid() };
        var store = new Store();
        var useCase = new RegisterAssetUseCase(actor, store);
        await useCase.ExecuteAsync(Request());
        actor.Id = Guid.NewGuid();
        await useCase.ExecuteAsync(Request());
        Assert.That(store.ActorId, Is.EqualTo(actor.Id));
        actor.Id = Guid.Empty;
        Assert.That((await useCase.ExecuteAsync(Request())).Error, Is.EqualTo(RegisterAssetError.Unauthenticated));
        Assert.That(actor.Reads, Is.EqualTo(3));
        Assert.That(store.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task NullInputsAndPreCancellationAvoidWork()
    {
        var actor = new Actor { Id = Guid.NewGuid() };
        var store = new Store();
        Assert.Throws<ArgumentNullException>(() => new RegisterAssetUseCase(null!, store));
        Assert.Throws<ArgumentNullException>(() => new RegisterAssetUseCase(actor, null!));
        var useCase = new RegisterAssetUseCase(actor, store);
        await Assert.ThrowsAsync<ArgumentNullException>(() => useCase.ExecuteAsync(null!));
        await Assert.ThrowsAsync<OperationCanceledException>(() => useCase.ExecuteAsync(Request(), new CancellationToken(true)));
        Assert.That(actor.Reads, Is.Zero);
        Assert.That(store.Calls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StorageExceptionsPropagateWithoutRetry(bool cancellation)
    {
        var completion = new TaskCompletionSource<AssetRegistrationOutcome>();
        var failure = cancellation ? (Exception)new OperationCanceledException() : new IOException("Unavailable.");
        completion.SetException(failure);
        var store = new Store { Completion = completion };
        var actual = await Assert.CatchAsync<Exception>(() => new RegisterAssetUseCase(new Actor { Id = Guid.NewGuid() }, store).ExecuteAsync(Request()));
        Assert.That(actual, Is.SameAs(failure));
        Assert.That(store.Calls, Is.EqualTo(1));
    }

    private static RegisterAssetRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), "Example");
    private sealed class Actor : ICurrentActor
    {
        public Guid? Id { get; set; }
        public int Reads { get; private set; }
        public Guid? ActorId { get { Reads++; return Id; } }
    }

    private sealed class Store : IAssetRegistrationStore
    {
        public AssetRegistrationOutcome Outcome { get; init; }
        public TaskCompletionSource<AssetRegistrationOutcome>? Completion { get; init; }
        public Asset? Asset { get; private set; }
        public Guid ActorId { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<AssetRegistrationOutcome> RegisterAsync(Asset asset, Guid actorId, CancellationToken cancellationToken)
        {
            Asset = asset;
            ActorId = actorId;
            Token = cancellationToken;
            Calls++;
            return Completion?.Task ?? Task.FromResult(Outcome);
        }
    }
}
