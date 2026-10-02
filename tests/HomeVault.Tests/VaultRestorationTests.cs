using HomeVault.Domain.Vaults;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class VaultRestorationTests
{
    [TestCase(VaultStatus.Active)]
    [TestCase(VaultStatus.Archived)]
    public void RestoresExactIndependentSnapshotAndPreservesDomainBehavior(VaultStatus status)
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var members = new List<KeyValuePair<Guid, VaultRole>> { new(owner, VaultRole.Owner), new(viewer, VaultRole.Viewer) };
        var vault = Vault.Restore(id, " Private name ", VaultType.Organization, status, members);
        members.Clear();
        Assert.That(vault.Id, Is.EqualTo(id));
        Assert.That(vault.Name, Is.EqualTo(" Private name "));
        Assert.That(vault.Type, Is.EqualTo(VaultType.Organization));
        Assert.That(vault.Status, Is.EqualTo(status));
        Assert.That(vault.Memberships.Count, Is.EqualTo(2));
        Assert.That(vault.Archive(viewer), Is.EqualTo(VaultArchiveError.NotOwner));
        Assert.That(vault.Archive(owner), Is.EqualTo(VaultArchiveError.None));
        Assert.That(vault.RemoveMember(viewer), Is.EqualTo(VaultMembershipError.Archived));
    }

    [TestCase("id")]
    [TestCase("name")]
    [TestCase("type")]
    [TestCase("status")]
    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("no-owner")]
    [TestCase("duplicate")]
    [TestCase("actor")]
    [TestCase("role")]
    public void RejectsInvalidStoredStateWithoutDisclosingValues(string defect)
    {
        var owner = Guid.NewGuid();
        var members = new List<KeyValuePair<Guid, VaultRole>> { new(owner, VaultRole.Owner) };
        if (defect == "empty") members.Clear();
        if (defect == "no-owner") members[0] = new(owner, VaultRole.Viewer);
        if (defect == "duplicate") members.Add(new(owner, VaultRole.Editor));
        if (defect == "actor") members.Add(new(Guid.Empty, VaultRole.Viewer));
        if (defect == "role") members.Add(new(Guid.NewGuid(), (VaultRole)99));
        var error = Assert.Throws<InvalidOperationException>(() => Vault.Restore(
            defect == "id" ? Guid.Empty : Guid.NewGuid(), defect == "name" ? " " : "Private sentinel",
            defect == "type" ? (VaultType)99 : VaultType.Personal,
            defect == "status" ? (VaultStatus)99 : VaultStatus.Active, defect == "null" ? null! : members));
        Assert.That(error!.Message, Is.EqualTo("Invalid stored Vault state."));
        Assert.That(error.InnerException, Is.Null);
    }
}
