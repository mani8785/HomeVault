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
    public void CreatesVaultForCurrentActorAndPreservesMetadata(VaultType type)
    {
        var actor = new TestActor { Identity = Guid.NewGuid() };
        var useCase = new CreateVaultUseCase(actor);
        var id = Guid.NewGuid();
        var result = useCase.Execute(new CreateVaultRequest(id, "  Example – København  ", type));
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
    public void MissingIdentityPrecedesInvalidRequestFields(bool empty)
    {
        var actor = new TestActor { Identity = empty ? Guid.Empty : null };
        var result = new CreateVaultUseCase(actor).Execute(new CreateVaultRequest(Guid.Empty, null, (VaultType)99));
        AssertFailure(result, CreateVaultError.Unauthenticated);
        Assert.That(actor.Reads, Is.EqualTo(1));
    }

    [Test]
    public void EmptyVaultIdentityPrecedesOtherDomainFailures()
    {
        var useCase = new CreateVaultUseCase(new TestActor { Identity = Guid.NewGuid() });
        AssertFailure(useCase.Execute(new CreateVaultRequest(Guid.Empty, null, (VaultType)99)), CreateVaultError.EmptyIdentity);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    [TestCase("\u2003")]
    public void InvalidNamePrecedesInvalidType(string? name)
    {
        var useCase = new CreateVaultUseCase(new TestActor { Identity = Guid.NewGuid() });
        AssertFailure(useCase.Execute(new CreateVaultRequest(Guid.NewGuid(), name, (VaultType)99)), CreateVaultError.BlankName);
    }

    [TestCase(-1)]
    [TestCase(3)]
    public void InvalidTypeMapsToApplicationError(int type)
    {
        var useCase = new CreateVaultUseCase(new TestActor { Identity = Guid.NewGuid() });
        AssertFailure(useCase.Execute(new CreateVaultRequest(Guid.NewGuid(), "Example", (VaultType)type)), CreateVaultError.InvalidType);
    }

    [Test]
    public void IdentityIsReadOncePerExecutionAndNeverCachedAcrossCalls()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var actor = new TestActor { Identity = firstId };
        var useCase = new CreateVaultUseCase(actor);
        var request = new CreateVaultRequest(Guid.NewGuid(), "Example", VaultType.Personal);
        var first = useCase.Execute(request);
        actor.Identity = secondId;
        var second = useCase.Execute(request);
        actor.Identity = null;
        AssertFailure(useCase.Execute(request), CreateVaultError.Unauthenticated);
        Assert.That(first.Vault!.InitialOwnerId, Is.EqualTo(firstId));
        Assert.That(second.Vault!.InitialOwnerId, Is.EqualTo(secondId));
        Assert.That(actor.Reads, Is.EqualTo(3));
    }

    [Test]
    public void NullProgrammingInputsAreRejectedExplicitly()
    {
        Assert.Throws<ArgumentNullException>(() => new CreateVaultUseCase(null!));
        var actor = new TestActor();
        Assert.Throws<ArgumentNullException>(() => new CreateVaultUseCase(actor).Execute(null!));
        Assert.That(actor.Reads, Is.Zero);
    }

    [Test]
    public void DiagnosticsDoNotFormatRequestOrResultMetadata()
    {
        var request = new CreateVaultRequest(Guid.NewGuid(), "Private fictional label", VaultType.Personal);
        var result = new CreateVaultUseCase(new TestActor { Identity = Guid.NewGuid() }).Execute(request);
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
