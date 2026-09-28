using System.Text.Json;
using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class SensitiveAttributeTests
{
    private const string Sentinel = "  fictional-sensitive-sentinel  ";
    private static Asset CreateAsset() => Asset.Create(Guid.NewGuid(), "Example record").Asset!;

    [TestCase(AttributeSensitivity.Ordinary)]
    [TestCase(AttributeSensitivity.Sensitive)]
    public void ClassificationControlsPropertyDisclosureButDeliberateReadPreservesText(AttributeSensitivity sensitivity)
    {
        var asset = CreateAsset();
        Assert.That(asset.AddAttribute("Label", Sentinel, sensitivity), Is.EqualTo(AssetAttributeError.None));
        var entry = asset.Attributes.Single();
        Assert.That(entry.Sensitivity, Is.EqualTo(sensitivity));
        Assert.That(entry.IsSensitive, Is.EqualTo(sensitivity == AttributeSensitivity.Sensitive));
        Assert.That(entry.Value, Is.EqualTo(sensitivity == AttributeSensitivity.Sensitive ? null : Sentinel));
        Assert.That(entry.ReadValue(), Is.EqualTo(Sentinel));
        Assert.That(entry.ToString(), Is.EqualTo("AssetAttribute"));
    }

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(int.MaxValue)]
    public void InvalidClassificationIsRejectedAfterTextValidationBeforeLookup(int invalid)
    {
        var asset = CreateAsset();
        asset.AddAttribute("Label", Sentinel, AttributeSensitivity.Sensitive);
        var before = asset.Attributes;
        var sensitivity = (AttributeSensitivity)invalid;
        Assert.That(asset.AddAttribute(null, null, sensitivity), Is.EqualTo(AssetAttributeError.BlankName));
        Assert.That(asset.AddAttribute("Label", null, sensitivity), Is.EqualTo(AssetAttributeError.BlankValue));
        Assert.That(asset.AddAttribute("Label", "replacement", sensitivity), Is.EqualTo(AssetAttributeError.InvalidSensitivity));
        Assert.That(asset.AddAttribute("New", "replacement", sensitivity), Is.EqualTo(AssetAttributeError.InvalidSensitivity));
        Assert.That(asset.Attributes, Is.EquivalentTo(before));
    }

    [TestCase(AttributeSensitivity.Ordinary)]
    [TestCase(AttributeSensitivity.Sensitive)]
    public void ReplacementPreservesClassificationAndEarlierSnapshots(AttributeSensitivity sensitivity)
    {
        var asset = CreateAsset();
        asset.AddAttribute("Label", Sentinel, sensitivity);
        var snapshot = asset.Attributes;
        Assert.That(asset.ChangeAttribute(" LABEL ", " replacement "), Is.EqualTo(AssetAttributeError.None));
        var changed = asset.Attributes.Single();
        Assert.That(changed.Sensitivity, Is.EqualTo(sensitivity));
        Assert.That(changed.ReadValue(), Is.EqualTo(" replacement "));
        Assert.That(changed.Name, Is.EqualTo("Label"));
        Assert.That(changed.Value, Is.EqualTo(sensitivity == AttributeSensitivity.Sensitive ? null : " replacement "));
        Assert.That(asset.RemoveAttribute("label"), Is.EqualTo(AssetAttributeError.None));
        Assert.That(asset.Attributes, Is.Empty);
        Assert.That(snapshot.Single().ReadValue(), Is.EqualTo(Sentinel));
    }

    [Test]
    public void DefaultJsonOfEntryAndAssetOmitsSensitiveTextButKeepsOrdinaryText()
    {
        var asset = CreateAsset();
        asset.AddAttribute("Private label", Sentinel, AttributeSensitivity.Sensitive);
        asset.AddAttribute("Material", "Steel", AttributeSensitivity.Ordinary);
        var entry = asset.Attributes.Single(item => item.IsSensitive);
        var entryJson = JsonSerializer.Serialize(entry);
        var assetJson = JsonSerializer.Serialize(asset);
        Assert.That(entryJson, Does.Not.Contain(Sentinel.Trim()));
        Assert.That(assetJson, Does.Not.Contain(Sentinel.Trim()));
        using var json = JsonDocument.Parse(entryJson);
        Assert.That(json.RootElement.GetProperty("Value").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(json.RootElement.GetProperty("IsSensitive").GetBoolean(), Is.True);
        Assert.That(assetJson, Does.Contain("Steel"));
        Assert.That(entry.ReadValue(), Is.EqualTo(Sentinel));
    }

    [Test]
    public void RejectedMutationsDoNotDowngradeOrExposeSensitiveValues()
    {
        var asset = CreateAsset();
        asset.AddAttribute("Private label", Sentinel, AttributeSensitivity.Sensitive);
        var before = asset.Attributes.Single();
        Assert.That(asset.AddAttribute("private label", "ordinary replacement", AttributeSensitivity.Ordinary).ToString(), Is.EqualTo("DuplicateName"));
        Assert.That(asset.ChangeAttribute("Private label", " ").ToString(), Is.EqualTo("BlankValue"));
        Assert.That(asset.ChangeAttribute("Missing", Sentinel).ToString(), Is.EqualTo("NotFound"));
        Assert.That(asset.Attributes.Single(), Is.SameAs(before));
        Assert.That(before.Value, Is.Null);
        Assert.That(before.ReadValue(), Is.EqualTo(Sentinel));
        Assert.That(before.ToString(), Does.Not.Contain("Private label").And.Not.Contain(Sentinel));
    }
}
