using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class VaultBoundAssetTests
{
    [Test]
    public void PreservesBindingAndOriginalName()
    {
        var id = Guid.NewGuid();
        var vaultId = Guid.NewGuid();
        var asset = Asset.Create(id, vaultId, "  København  ").Asset!;
        Assert.That(asset.Id, Is.EqualTo(id));
        Assert.That(asset.VaultId, Is.EqualTo(vaultId));
        Assert.That(asset.Name, Is.EqualTo("  København  "));
        Assert.That(asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary), Is.EqualTo(AssetAttributeError.None));
        Assert.That(asset.VaultId, Is.EqualTo(vaultId));
        Assert.That(Asset.Create(Guid.NewGuid(), "Unbound example").Asset!.VaultId, Is.Null);
    }

    [TestCase(0, AssetCreationError.EmptyIdentity)]
    [TestCase(1, AssetCreationError.EmptyVaultIdentity)]
    [TestCase(2, AssetCreationError.BlankName)]
    public void ValidationOrderLeavesNoPartialAsset(int validIds, AssetCreationError expected)
    {
        var result = Asset.Create(validIds > 0 ? Guid.NewGuid() : Guid.Empty,
            validIds > 1 ? Guid.NewGuid() : Guid.Empty, " ");
        Assert.That(result.Error, Is.EqualTo(expected));
        Assert.That(result.Asset, Is.Null);
    }
}
