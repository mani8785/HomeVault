using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class AssetAttributeTests
{
    private static Asset CreateAsset() => Asset.Create(Guid.NewGuid(), "Example record").Asset!;

    [TestCase(" Material ", "  Steel  ", "Material")]
    [TestCase("Provider", "Example insurer", "Provider")]
    [TestCase("\u2003Note\u2003", "Cykel – København", "Note")]
    public void AddPreservesTextAndTrimsName(string name, string value, string expectedName)
    {
        var asset = CreateAsset();
        Assert.That(asset.AddAttribute(name, value, AttributeSensitivity.Ordinary), Is.EqualTo(AssetAttributeError.None));
        Assert.That(asset.Attributes.Single().Name, Is.EqualTo(expectedName));
        Assert.That(asset.Attributes.Single().Value, Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    [TestCase("\u2003")]
    public void InvalidNamesAreRejectedBeforeValuesWithoutMutation(string? name)
    {
        var asset = CreateAsset();
        asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary);
        Assert.That(asset.AddAttribute(name, null, AttributeSensitivity.Ordinary), Is.EqualTo(AssetAttributeError.BlankName));
        Assert.That(asset.ChangeAttribute(name, null), Is.EqualTo(AssetAttributeError.BlankName));
        Assert.That(asset.RemoveAttribute(name), Is.EqualTo(AssetAttributeError.BlankName));
        Assert.That(asset.Attributes.Single().Value, Is.EqualTo("Steel"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    [TestCase("\u2003")]
    public void InvalidValuesAreRejectedBeforeLookupWithoutMutation(string? value)
    {
        var asset = CreateAsset();
        asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary);
        Assert.That(asset.AddAttribute("material", value, AttributeSensitivity.Ordinary), Is.EqualTo(AssetAttributeError.BlankValue));
        Assert.That(asset.ChangeAttribute("Missing", value), Is.EqualTo(AssetAttributeError.BlankValue));
        Assert.That(asset.ChangeAttribute("Material", value), Is.EqualTo(AssetAttributeError.BlankValue));
        Assert.That(asset.Attributes.Single().Value, Is.EqualTo("Steel"));
    }

    [Test]
    public void DuplicateAddAndMissingMutationsPreserveExistingEntries()
    {
        var asset = CreateAsset();
        asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary);
        asset.AddAttribute("Color", "Blue", AttributeSensitivity.Ordinary);
        var before = asset.Attributes;
        Assert.That(asset.AddAttribute(" MATERIAL ", "Wood", AttributeSensitivity.Ordinary), Is.EqualTo(AssetAttributeError.DuplicateName));
        Assert.That(asset.ChangeAttribute("Missing", "Text"), Is.EqualTo(AssetAttributeError.NotFound));
        Assert.That(asset.RemoveAttribute("Missing"), Is.EqualTo(AssetAttributeError.NotFound));
        Assert.That(asset.Attributes, Is.EquivalentTo(before));
    }

    [Test]
    public void ChangeAndRemoveMatchNamesAndPreserveOtherEntries()
    {
        var asset = CreateAsset();
        asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary);
        asset.AddAttribute("Color", "Blue", AttributeSensitivity.Ordinary);
        Assert.That(asset.ChangeAttribute(" MATERIAL ", " Wood "), Is.EqualTo(AssetAttributeError.None));
        var changed = asset.Attributes.Single(entry => entry.Name == "Material");
        Assert.That(changed.Value, Is.EqualTo(" Wood "));
        Assert.That(asset.ChangeAttribute("material", " Wood "), Is.EqualTo(AssetAttributeError.None));
        Assert.That(asset.Attributes.Count, Is.EqualTo(2));
        Assert.That(asset.RemoveAttribute(" material "), Is.EqualTo(AssetAttributeError.None));
        Assert.That(asset.RemoveAttribute("Material"), Is.EqualTo(AssetAttributeError.NotFound));
        Assert.That(asset.Attributes.Single().Name, Is.EqualTo("Color"));
        Assert.That(asset.Attributes.Single().Value, Is.EqualTo("Blue"));
    }

    [Test]
    public void SnapshotsCannotMutateAssetAndRemainStableAfterChanges()
    {
        var asset = CreateAsset();
        var empty = asset.Attributes;
        asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary);
        var snapshot = asset.Attributes;
        Assert.Throws<NotSupportedException>(() => ((IList<AssetAttribute>)snapshot).Clear());
        asset.ChangeAttribute("Material", "Wood");
        asset.RemoveAttribute("Material");
        Assert.That(empty, Is.Empty);
        Assert.That(snapshot.Single().Value, Is.EqualTo("Steel"));
        Assert.That(asset.Attributes, Is.Empty);
    }

    [Test]
    public void AttributesAreScopedToOneAssetAndDiagnosticsDoNotExposeText()
    {
        var first = CreateAsset();
        var second = CreateAsset();
        first.AddAttribute("Private example name", "Private example text", AttributeSensitivity.Ordinary);
        Assert.That(second.AddAttribute("Private example name", "Other text", AttributeSensitivity.Ordinary), Is.EqualTo(AssetAttributeError.None));
        Assert.That(first.Attributes.Single().ToString(), Is.EqualTo("AssetAttribute"));
        Assert.That(first.AddAttribute("Private example name", "Secret example", AttributeSensitivity.Ordinary).ToString(), Is.EqualTo("DuplicateName"));
        first.RemoveAttribute("Private example name");
        Assert.That(second.Attributes.Single().Value, Is.EqualTo("Other text"));
    }
}
