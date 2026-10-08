using HomeVault.Application.Assets;
using HomeVault.Application.Identity;
using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

public sealed class SensitiveAttributeUseCasesTests
{
    [TestCase(false), TestCase(true)]
    public async Task TrustedIdentityIsReadOnceAndAbsentIdentityNeverReachesStorage(bool authenticated)
    {
        var actor = new Actor { Id = authenticated ? Guid.NewGuid() : null }; var store = new Store();
        var useCase = new SensitiveAttributeUseCases(actor, store);
        var result = await useCase.AddAsync(Guid.NewGuid(), "label", "fictional", AttributeSensitivity.Sensitive);
        Assert.That(actor.Reads, Is.EqualTo(1));
        Assert.That(store.ActorId, Is.EqualTo(actor.Id));
        Assert.That(result.Outcome, Is.EqualTo(authenticated ? SensitiveAttributeOutcome.Succeeded : SensitiveAttributeOutcome.Unauthenticated));
    }
    [Test]
    public async Task InvalidIdentityAndCancellationNeverReachStorage()
    {
        var actor = new Actor { Id = Guid.NewGuid() }; var store = new Store(); var useCase = new SensitiveAttributeUseCases(actor, store);
        Assert.That((await useCase.ReadAsync(Guid.NewGuid(), Guid.Empty)).Outcome, Is.EqualTo(SensitiveAttributeOutcome.InvalidIdentity));
        Assert.That(store.ActorId, Is.Null);
        var reads = actor.Reads;
        Assert.Throws<OperationCanceledException>(() => useCase.ListAsync(Guid.NewGuid(), new CancellationToken(true)));
        Assert.That(actor.Reads, Is.EqualTo(reads));
    }
    [TestCase(" ", "x", AssetAttributeError.BlankName)]
    [TestCase("name", " ", AssetAttributeError.BlankValue)]
    public void CandidateValidationPreservesDomainRules(string name, string value, AssetAttributeError error)
    { Assert.That(AssetAttribute.Create(name, value, AttributeSensitivity.Sensitive).Error, Is.EqualTo(error)); }
    private sealed class Actor : ICurrentActor
    { internal Guid? Id; internal int Reads; public Guid? ActorId { get { Reads++; return Id; } } }
    private sealed class Store : ISensitiveAttributeStore
    {
        internal Guid? ActorId;
        public Task<SensitiveAttributeResult> AddAsync(Guid assetId, Guid actorId, string? name, string? value, AttributeSensitivity sensitivity, CancellationToken token)
        { ActorId = actorId; return Task.FromResult(SensitiveAttributeResult.Added(Guid.NewGuid())); }
        public Task<SensitiveAttributeResult> ChangeAsync(Guid assetId, Guid actorId, Guid attributeId, string? value, CancellationToken token) => throw new AssertionException("Unexpected storage call");
        public Task<SensitiveAttributeResult> RemoveAsync(Guid assetId, Guid actorId, Guid attributeId, CancellationToken token) => throw new AssertionException("Unexpected storage call");
        public Task<SensitiveAttributeResult> ListAsync(Guid assetId, Guid actorId, CancellationToken token) => throw new AssertionException("Unexpected storage call");
        public Task<SensitiveAttributeResult> ReadAsync(Guid assetId, Guid actorId, Guid attributeId, CancellationToken token) => throw new AssertionException("Unexpected storage call");
    }
}
