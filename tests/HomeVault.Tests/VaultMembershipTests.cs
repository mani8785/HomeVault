using HomeVault.Domain.Vaults;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class VaultMembershipTests
{
    private Guid _ownerId;
    private Vault _vault = null!;

    [SetUp]
    public void CreateVault()
    {
        _ownerId = Guid.NewGuid();
        _vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, _ownerId).Vault!;
    }

    [TestCase(VaultRole.Owner)]
    [TestCase(VaultRole.Administrator)]
    [TestCase(VaultRole.Editor)]
    [TestCase(VaultRole.Viewer)]
    public void AddsEachRoleWithoutChangingInitialOwner(VaultRole role)
    {
        var actorId = Guid.NewGuid();
        Assert.That(_vault.AddMember(actorId, role), Is.EqualTo(VaultMembershipError.None));
        Assert.That(_vault.Memberships.Count, Is.EqualTo(2));
        Assert.That(_vault.Memberships.Single(member => member.ActorId == actorId).Role, Is.EqualTo(role));
        Assert.That(_vault.Memberships.Single(member => member.ActorId == _ownerId).Role, Is.EqualTo(VaultRole.Owner));
    }

    [Test]
    public void DuplicateAndMissingMembersFailWithoutMutation()
    {
        var before = _vault.Memberships;
        Assert.That(_vault.AddMember(_ownerId, VaultRole.Viewer), Is.EqualTo(VaultMembershipError.DuplicateMember));
        Assert.That(_vault.ChangeMemberRole(Guid.NewGuid(), VaultRole.Editor), Is.EqualTo(VaultMembershipError.MemberNotFound));
        Assert.That(_vault.RemoveMember(Guid.NewGuid()), Is.EqualTo(VaultMembershipError.MemberNotFound));
        Assert.That(_vault.Memberships, Is.EquivalentTo(before));
    }

    [TestCase(-1)]
    [TestCase(4)]
    [TestCase(int.MaxValue)]
    public void ValidationPrecedesLookupAndNeverMutates(int invalidRole)
    {
        var before = _vault.Memberships;
        Assert.That(_vault.AddMember(Guid.Empty, (VaultRole)invalidRole), Is.EqualTo(VaultMembershipError.EmptyActorIdentity));
        Assert.That(_vault.ChangeMemberRole(Guid.Empty, (VaultRole)invalidRole), Is.EqualTo(VaultMembershipError.EmptyActorIdentity));
        Assert.That(_vault.RemoveMember(Guid.Empty), Is.EqualTo(VaultMembershipError.EmptyActorIdentity));
        Assert.That(_vault.AddMember(_ownerId, (VaultRole)invalidRole), Is.EqualTo(VaultMembershipError.InvalidRole));
        Assert.That(_vault.ChangeMemberRole(Guid.NewGuid(), (VaultRole)invalidRole), Is.EqualTo(VaultMembershipError.InvalidRole));
        Assert.That(_vault.Memberships, Is.EquivalentTo(before));
    }

    [TestCase(VaultRole.Administrator)]
    [TestCase(VaultRole.Editor)]
    [TestCase(VaultRole.Viewer)]
    public void LastOwnerCannotBeDemotedOrRemoved(VaultRole replacement)
    {
        _vault.AddMember(Guid.NewGuid(), VaultRole.Administrator);
        var before = _vault.Memberships;
        Assert.That(_vault.ChangeMemberRole(_ownerId, replacement), Is.EqualTo(VaultMembershipError.LastOwner));
        Assert.That(_vault.RemoveMember(_ownerId), Is.EqualTo(VaultMembershipError.LastOwner));
        Assert.That(_vault.Memberships, Is.EquivalentTo(before));
    }

    [Test]
    public void SameRoleIsSuccessfulWithoutReplacingLastOwner()
    {
        var before = _vault.Memberships.Single();
        Assert.That(_vault.ChangeMemberRole(_ownerId, VaultRole.Owner), Is.EqualTo(VaultMembershipError.None));
        Assert.That(_vault.Memberships.Single(), Is.SameAs(before));
    }

    [Test]
    public void OwnershipCanBeTransferredByPromotionBeforeDemotion()
    {
        var nextOwner = Guid.NewGuid();
        _vault.AddMember(nextOwner, VaultRole.Viewer);
        Assert.That(_vault.ChangeMemberRole(nextOwner, VaultRole.Owner), Is.EqualTo(VaultMembershipError.None));
        Assert.That(_vault.ChangeMemberRole(_ownerId, VaultRole.Editor), Is.EqualTo(VaultMembershipError.None));
        Assert.That(_vault.RemoveMember(_ownerId), Is.EqualTo(VaultMembershipError.None));
        Assert.That(_vault.RemoveMember(nextOwner), Is.EqualTo(VaultMembershipError.LastOwner));
        Assert.That(_vault.Memberships.Single().ActorId, Is.EqualTo(nextOwner));
        Assert.That(_vault.Memberships.Single().Role, Is.EqualTo(VaultRole.Owner));
    }

    [Test]
    public void OneOfTwoOwnersCanBeRemovedButRemainingOwnerIsProtected()
    {
        var otherOwner = Guid.NewGuid();
        _vault.AddMember(otherOwner, VaultRole.Owner);
        Assert.That(_vault.RemoveMember(_ownerId), Is.EqualTo(VaultMembershipError.None));
        Assert.That(_vault.RemoveMember(otherOwner), Is.EqualTo(VaultMembershipError.LastOwner));
        Assert.That(_vault.ChangeMemberRole(otherOwner, VaultRole.Viewer), Is.EqualTo(VaultMembershipError.LastOwner));
    }

    [Test]
    public void SnapshotsRemainImmutableAcrossAddChangeAndRemove()
    {
        var initial = _vault.Memberships;
        var actorId = Guid.NewGuid();
        _vault.AddMember(actorId, VaultRole.Editor);
        var beforeChange = _vault.Memberships;
        Assert.Throws<NotSupportedException>(() => ((IList<VaultMembership>)beforeChange).Clear());
        Assert.That(_vault.ChangeMemberRole(actorId, VaultRole.Viewer), Is.EqualTo(VaultMembershipError.None));
        Assert.That(_vault.Memberships.Single(member => member.ActorId == actorId).Role, Is.EqualTo(VaultRole.Viewer));
        Assert.That(_vault.RemoveMember(actorId), Is.EqualTo(VaultMembershipError.None));
        Assert.That(initial.Count, Is.EqualTo(1));
        Assert.That(beforeChange.Single(member => member.ActorId == actorId).Role, Is.EqualTo(VaultRole.Editor));
        Assert.That(_vault.Memberships.Single().ActorId, Is.EqualTo(_ownerId));
    }

    [TestCase(VaultType.Personal)]
    [TestCase(VaultType.Household)]
    [TestCase(VaultType.Organization)]
    public void MembershipIsScopedToVaultAndUsesSameRulesForEveryType(VaultType type)
    {
        var other = Vault.Create(Guid.NewGuid(), "Other example", type, _ownerId).Vault!;
        var actorId = Guid.NewGuid();
        _vault.AddMember(actorId, VaultRole.Editor);
        Assert.That(other.AddMember(actorId, VaultRole.Viewer), Is.EqualTo(VaultMembershipError.None));
        _vault.RemoveMember(actorId);
        Assert.That(other.Memberships.Single(member => member.ActorId == actorId).Role, Is.EqualTo(VaultRole.Viewer));
        Assert.That(other.RemoveMember(_ownerId), Is.EqualTo(VaultMembershipError.LastOwner));
    }
}
