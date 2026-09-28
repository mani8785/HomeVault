using HomeVault.Domain.Vaults;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class VaultCreationTests
{
    [TestCase(VaultType.Personal)]
    [TestCase(VaultType.Household)]
    [TestCase(VaultType.Organization)]
    public void CreationPreservesIdentityAndCreatesExactlyOneOwner(VaultType type)
    {
        var id = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var result = Vault.Create(id, "Example Vault", type, ownerId);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Error, Is.EqualTo(VaultCreationError.None));
        Assert.That(result.Vault, Is.Not.Null);
        var vault = result.Vault!;
        Assert.That(vault.Id, Is.EqualTo(id));
        Assert.That(vault.Type, Is.EqualTo(type));
        Assert.That(vault.Status, Is.EqualTo(VaultStatus.Active));
        Assert.That(vault.Memberships.Single().ActorId, Is.EqualTo(ownerId));
        Assert.That(vault.Memberships.Single().Role, Is.EqualTo(VaultRole.Owner));
    }

    [TestCase("x")]
    [TestCase("  Example household  ")]
    [TestCase("Hjem – København")]
    public void ValidNamesArePreservedExactly(string name)
    {
        var result = Vault.Create(Guid.NewGuid(), name, VaultType.Household, Guid.NewGuid());
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Vault!.Name, Is.EqualTo(name));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    [TestCase("\u2003")]
    public void BlankNamesFailBeforeTypeAndOwner(string? name)
    {
        AssertFailure(Vault.Create(Guid.NewGuid(), name, (VaultType)99, Guid.Empty), VaultCreationError.BlankName);
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(int.MaxValue)]
    public void UnsupportedTypesFailBeforeOwner(int type)
    {
        AssertFailure(Vault.Create(Guid.NewGuid(), "Example", (VaultType)type, Guid.Empty), VaultCreationError.InvalidType);
    }

    [Test]
    public void EmptyIdentityFailsBeforeAllOtherInputs()
    {
        AssertFailure(Vault.Create(Guid.Empty, null, (VaultType)99, Guid.Empty), VaultCreationError.EmptyIdentity);
    }

    [Test]
    public void EmptyOwnerFailsWithoutReturningAPartialVault()
    {
        AssertFailure(Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, Guid.Empty), VaultCreationError.EmptyOwnerIdentity);
    }

    [Test]
    public void MembershipCannotBeChangedThroughTheExposedCollection()
    {
        var ownerId = Guid.NewGuid();
        var vault = Vault.Create(Guid.NewGuid(), "Example", VaultType.Personal, ownerId).Vault!;
        var memberships = (IList<VaultMembership>)vault.Memberships;
        Assert.Throws<NotSupportedException>(() => memberships.Clear());
        Assert.Throws<NotSupportedException>(() => memberships[0] = memberships[0]);
        Assert.That(vault.Memberships.Single().ActorId, Is.EqualTo(ownerId));
        Assert.That(vault.Memberships.Single().Role, Is.EqualTo(VaultRole.Owner));
    }

    [Test]
    public void DiagnosticsDoNotFormatNamesOrActorIdentities()
    {
        var ownerId = Guid.NewGuid();
        var result = Vault.Create(Guid.NewGuid(), "Private fictional name", VaultType.Organization, ownerId);
        Assert.That(result.ToString(), Is.EqualTo("VaultCreationResult"));
        Assert.That(result.Vault!.ToString(), Is.EqualTo("Vault"));
        Assert.That(result.Vault.Memberships.Single().ToString(), Is.EqualTo("VaultMembership"));
        var failed = Vault.Create(Guid.Empty, "Private fictional name", VaultType.Organization, ownerId);
        Assert.That(failed.ToString(), Is.EqualTo("VaultCreationResult"));
        Assert.That(failed.Error.ToString(), Is.EqualTo("EmptyIdentity"));
    }

    private static void AssertFailure(VaultCreationResult result, VaultCreationError expected)
    {
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Vault, Is.Null);
        Assert.That(result.Error, Is.EqualTo(expected));
    }
}
