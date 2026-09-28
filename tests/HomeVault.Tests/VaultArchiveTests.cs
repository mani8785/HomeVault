using HomeVault.Domain.Vaults;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class VaultArchiveTests
{
    [TestCase(VaultType.Personal)]
    [TestCase(VaultType.Household)]
    [TestCase(VaultType.Organization)]
    public void OwnerArchivesAndRetainsAllReadableInformation(VaultType type)
    {
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var vault = Vault.Create(id, " Example ", type, owner).Vault!;
        vault.AddMember(Guid.NewGuid(), VaultRole.Viewer);
        var snapshot = vault.Memberships;
        Assert.That(vault.Archive(owner), Is.EqualTo(VaultArchiveError.None));
        Assert.That(vault.Status, Is.EqualTo(VaultStatus.Archived));
        Assert.That(vault.Id, Is.EqualTo(id));
        Assert.That(vault.Name, Is.EqualTo(" Example "));
        Assert.That(vault.Type, Is.EqualTo(type));
        Assert.That(vault.Memberships, Is.EquivalentTo(snapshot));
        Assert.That(vault.Archive(owner), Is.EqualTo(VaultArchiveError.None));
        Assert.That(vault.Memberships, Is.EquivalentTo(snapshot));
    }

    [TestCase(VaultRole.Administrator)]
    [TestCase(VaultRole.Editor)]
    [TestCase(VaultRole.Viewer)]
    public void NonOwnersAreRejectedBeforeAndAfterArchive(VaultRole role)
    {
        var owner = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Household, owner).Vault!;
        vault.AddMember(actor, role);
        var snapshot = vault.Memberships;
        Assert.That(vault.Archive(actor), Is.EqualTo(VaultArchiveError.NotOwner));
        Assert.That(vault.Status, Is.EqualTo(VaultStatus.Active));
        vault.Archive(owner);
        Assert.That(vault.Archive(actor), Is.EqualTo(VaultArchiveError.NotOwner));
        Assert.That(vault.Status, Is.EqualTo(VaultStatus.Archived));
        Assert.That(vault.Memberships, Is.EquivalentTo(snapshot));
    }

    [Test]
    public void UnknownAndEmptyActorsFailEvenForAlreadyArchivedVault()
    {
        var owner = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, owner).Vault!;
        Assert.That(vault.Archive(Guid.Empty), Is.EqualTo(VaultArchiveError.EmptyActorIdentity));
        Assert.That(vault.Archive(Guid.NewGuid()), Is.EqualTo(VaultArchiveError.NotOwner));
        Assert.That(vault.Status, Is.EqualTo(VaultStatus.Active));
        vault.Archive(owner);
        Assert.That(vault.Archive(Guid.Empty), Is.EqualTo(VaultArchiveError.EmptyActorIdentity));
        Assert.That(vault.Archive(Guid.NewGuid()), Is.EqualTo(VaultArchiveError.NotOwner));
    }

    [Test]
    public void EveryMembershipMutationIsBlockedIncludingNoOpsAndInvalidInputs()
    {
        var owner = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Organization, owner).Vault!;
        vault.AddMember(viewer, VaultRole.Viewer);
        vault.Archive(owner);
        var snapshot = vault.Memberships;
        Assert.That(vault.AddMember(Guid.NewGuid(), VaultRole.Editor), Is.EqualTo(VaultMembershipError.Archived));
        Assert.That(vault.AddMember(viewer, VaultRole.Viewer), Is.EqualTo(VaultMembershipError.Archived));
        Assert.That(vault.AddMember(Guid.Empty, (VaultRole)99), Is.EqualTo(VaultMembershipError.Archived));
        Assert.That(vault.ChangeMemberRole(viewer, VaultRole.Editor), Is.EqualTo(VaultMembershipError.Archived));
        Assert.That(vault.ChangeMemberRole(owner, VaultRole.Owner), Is.EqualTo(VaultMembershipError.Archived));
        Assert.That(vault.ChangeMemberRole(Guid.Empty, (VaultRole)99), Is.EqualTo(VaultMembershipError.Archived));
        Assert.That(vault.RemoveMember(viewer), Is.EqualTo(VaultMembershipError.Archived));
        Assert.That(vault.RemoveMember(owner), Is.EqualTo(VaultMembershipError.Archived));
        Assert.That(vault.RemoveMember(Guid.Empty), Is.EqualTo(VaultMembershipError.Archived));
        Assert.That(vault.Memberships, Is.EquivalentTo(snapshot));
    }

    [Test]
    public void CurrentOwnershipIsUsedAndOtherVaultsRemainActive()
    {
        var originalOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, originalOwner).Vault!;
        var other = Vault.Create(Guid.NewGuid(), "Other", VaultType.Personal, originalOwner).Vault!;
        vault.AddMember(newOwner, VaultRole.Owner);
        vault.ChangeMemberRole(originalOwner, VaultRole.Viewer);
        Assert.That(vault.Archive(originalOwner), Is.EqualTo(VaultArchiveError.NotOwner));
        Assert.That(vault.Archive(newOwner), Is.EqualTo(VaultArchiveError.None));
        Assert.That(other.Status, Is.EqualTo(VaultStatus.Active));
        Assert.That(other.AddMember(Guid.NewGuid(), VaultRole.Editor), Is.EqualTo(VaultMembershipError.None));
    }
}
