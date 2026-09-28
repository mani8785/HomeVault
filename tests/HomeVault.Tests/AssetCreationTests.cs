using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class AssetCreationTests
{
    [TestCase("Bicycle")]
    [TestCase("  Insurance policy  ")]
    [TestCase("Cykel – købt i København")]
    public void ValidCreationPreservesCallerIdentityAndName(string name)
    {
        var id = Guid.NewGuid();

        var result = Asset.Create(id, name);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.EqualTo(AssetCreationError.None));
            Assert.That(result.Asset, Is.Not.Null);
            Assert.That(result.Asset?.Id, Is.EqualTo(id));
            Assert.That(result.Asset?.Name, Is.EqualTo(name));
        }
    }

    [Test]
    public void EmptyIdentityIsRejectedWithoutCreatingAnAsset()
    {
        var result = Asset.Create(Guid.Empty, "Bicycle");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Asset, Is.Null);
            Assert.That(result.Error, Is.EqualTo(AssetCreationError.EmptyIdentity));
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\r\n")]
    [TestCase("\u2003")]
    public void BlankNameIsRejectedWithoutCreatingAnAsset(string? name)
    {
        var result = Asset.Create(Guid.NewGuid(), name);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Asset, Is.Null);
            Assert.That(result.Error, Is.EqualTo(AssetCreationError.BlankName));
        }
    }

    [Test]
    public void EmptyIdentityTakesPrecedenceWhenBothInputsAreInvalid()
    {
        var result = Asset.Create(Guid.Empty, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Asset, Is.Null);
            Assert.That(result.Error, Is.EqualTo(AssetCreationError.EmptyIdentity));
        }
    }

    [Test]
    public void FailureDiagnosticsDoNotEchoTheSuppliedName()
    {
        const string exampleName = "Private example record";

        var result = Asset.Create(Guid.Empty, exampleName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Asset, Is.Null);
            Assert.That(result.Error.ToString(), Does.Not.Contain(exampleName));
            Assert.That(result.ToString(), Does.Not.Contain(exampleName));
        }
    }
}
