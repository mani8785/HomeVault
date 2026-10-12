using HomeVault.Application.Identity;
using HomeVault.Application.Records;
using HomeVault.Domain;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using NUnit.Framework;

namespace HomeVault.Tests;

public sealed class RenameTests
{
    [Test]
    public void AssetRenamePreservesOwnedDataAndExactName()
    {
        var id = Guid.NewGuid(); var vaultId = Guid.NewGuid();
        var asset = Asset.Create(id, vaultId, "Original").Asset!;
        asset.AddAttribute("Private", "Fictional secret", AttributeSensitivity.Sensitive);
        asset.AddEvidence(Guid.NewGuid(), "Note", EvidenceKind.Note, "Fictional note");
        var attributes = asset.Attributes; var evidence = asset.Evidence;
        Assert.That(asset.Rename(" Étage/طبقه "), Is.EqualTo(RenameError.None));
        Assert.That(asset.Name, Is.EqualTo(" Étage/طبقه "));
        Assert.That(asset.Id, Is.EqualTo(id)); Assert.That(asset.VaultId, Is.EqualTo(vaultId));
        Assert.That(asset.Attributes, Is.EqualTo(attributes)); Assert.That(asset.Evidence, Is.EqualTo(evidence));
    }

    [TestCase(null), TestCase(""), TestCase(" \t")]
    public void BlankNamePreservesAssetAndVaultState(string? name)
    {
        var asset = Asset.Create(Guid.NewGuid(), "Original").Asset!;
        var vault = Vault.Create(Guid.NewGuid(), "Original", VaultType.Household, Guid.NewGuid()).Vault!;
        Assert.That(asset.Rename(name), Is.EqualTo(RenameError.BlankName));
        Assert.That(vault.Rename(name), Is.EqualTo(RenameError.BlankName));
        Assert.That(asset.Name, Is.EqualTo("Original")); Assert.That(vault.Name, Is.EqualTo("Original"));
    }

    [Test]
    public void VaultRenamePreservesMembershipAndArchivedRejectsEvenUnchangedName()
    {
        var owner = Guid.NewGuid(); var vault = Vault.Create(Guid.NewGuid(), "Original", VaultType.Organization, owner).Vault!;
        vault.AddMember(Guid.NewGuid(), VaultRole.Editor); var members = vault.Memberships;
        Assert.That(vault.Rename(" Renamed "), Is.EqualTo(RenameError.None));
        Assert.That(vault.Type, Is.EqualTo(VaultType.Organization)); Assert.That(vault.Memberships, Is.EqualTo(members));
        vault.Archive(owner);
        Assert.That(vault.Rename(" Renamed "), Is.EqualTo(RenameError.Archived));
        Assert.That(vault.Rename(null), Is.EqualTo(RenameError.Archived));
        Assert.That(vault.Name, Is.EqualTo(" Renamed "));
    }

    [TestCase(false), TestCase(true)]
    public async Task TrustedActorReadOnceAndUnauthenticatedInvalidOrCancelledCallsNeverReachStore(bool vault)
    {
        var actor = new Actor(); var store = new Store(); var useCases = new RenameRecords(actor, store); var id = Guid.NewGuid();
        Task<RenameOutcome> Call(CancellationToken token = default) => vault ? useCases.VaultAsync(id, " New ", " Old ", token) : useCases.AssetAsync(id, " New ", " Old ", token);
        Assert.That(await Call(), Is.EqualTo(RenameOutcome.Unauthenticated)); Assert.That(store.Calls, Is.Zero);
        actor.Id = Guid.NewGuid(); id = Guid.Empty;
        Assert.That(await Call(), Is.EqualTo(RenameOutcome.InvalidIdentity)); Assert.That(store.Calls, Is.Zero);
        id = Guid.NewGuid(); using var source = new CancellationTokenSource();
        Assert.That(await Call(source.Token), Is.EqualTo(RenameOutcome.Conflict));
        Assert.That(actor.Reads, Is.EqualTo(3)); Assert.That(store.ActorId, Is.EqualTo(actor.Id));
        Assert.That(store.Id, Is.EqualTo(id)); Assert.That(store.Name, Is.EqualTo(" New ")); Assert.That(store.Expected, Is.EqualTo(" Old "));
        Assert.That(store.Token, Is.EqualTo(source.Token)); Assert.That(store.IsVault, Is.EqualTo(vault));
        source.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => Call(source.Token));
        Assert.That(actor.Reads, Is.EqualTo(3)); Assert.That(store.Calls, Is.EqualTo(1));
        store.Outcome = (RenameOutcome)999;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Call());
    }

    private sealed class Actor : ICurrentActor
    {
        internal Guid? Id; internal int Reads;
        public Guid? ActorId { get { Reads++; return Id; } }
    }
    private sealed class Store : IRecordNameStore
    {
        internal int Calls; internal Guid ActorId, Id; internal string? Name, Expected; internal CancellationToken Token; internal bool IsVault;
        internal RenameOutcome Outcome = RenameOutcome.Conflict;
        public Task<RenameOutcome> RenameVaultAsync(Guid id, Guid actor, string? name, string? expectedName, CancellationToken token) => Record(id, actor, name, expectedName, token, true);
        public Task<RenameOutcome> RenameAssetAsync(Guid id, Guid actor, string? name, string? expectedName, CancellationToken token) => Record(id, actor, name, expectedName, token, false);
        private Task<RenameOutcome> Record(Guid id, Guid actor, string? name, string? expectedName, CancellationToken token, bool vault)
        { Calls++; Id = id; ActorId = actor; Name = name; Expected = expectedName; Token = token; IsVault = vault; return Task.FromResult(Outcome); }
    }
}
