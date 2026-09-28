using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class AssetValueObjectTests
{
    [Test]
    public void IdentityPreservesValueAndSupportsHashBasedLookup()
    {
        var guid = Guid.NewGuid();
        var identity = new AssetId(guid);
        var equalIdentity = new AssetId(guid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity.Value, Is.EqualTo(guid));
            Assert.That(identity == equalIdentity, Is.True);
            Assert.That(identity.Equals((object)equalIdentity), Is.True);
            Assert.That(new HashSet<AssetId> { identity }.Contains(equalIdentity), Is.True);
            Assert.That(identity != new AssetId(Guid.NewGuid()), Is.True);
            Assert.That(identity == null, Is.False);
        }
    }

    [Test]
    public void EmptyIdentityIsRejectedAtConstruction()
    {
        var error = Assert.Throws<ArgumentException>(() => new AssetId(Guid.Empty));
        Assert.That(error!.ParamName, Is.EqualTo("id"));
    }

    [TestCase("x")]
    [TestCase("  Bicycle  ")]
    [TestCase("Cykel – København")]
    public void NamePreservesItsExactValue(string name)
    {
        var value = new AssetName(name);
        Assert.That(value.Value, Is.EqualTo(name));
    }

    [Test]
    public void NameHasNoUnapprovedMaximumLength()
    {
        var name = new string('x', 10000);
        Assert.That(new AssetName(name).Value, Is.EqualTo(name));
    }

    [Test]
    public void NullNameIsRejectedAtConstruction()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new AssetName(null));
        Assert.That(error!.ParamName, Is.EqualTo("name"));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t\r\n")]
    [TestCase("\u2003")]
    public void BlankNameIsRejectedAtConstruction(string name)
    {
        var error = Assert.Throws<ArgumentException>(() => new AssetName(name));
        Assert.That(error!.ParamName, Is.EqualTo("name"));
    }

    [Test]
    public void EqualNamesSupportValueComparisonAndHashBasedLookup()
    {
        var first = new AssetName("Bicycle");
        var second = new AssetName(new string("Bicycle".ToCharArray()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first == second, Is.True);
            Assert.That(first.Equals((object)second), Is.True);
            Assert.That(new HashSet<AssetName> { first }.Contains(second), Is.True);
            Assert.That(first == null, Is.False);
        }
    }

    [TestCase("Bicycle", "bicycle")]
    [TestCase("Bicycle", " Bicycle ")]
    [TestCase("\u00e9", "e\u0301")]
    public void DifferentExactRepresentationsAreNotEqual(string left, string right)
    {
        Assert.That(new AssetName(left) != new AssetName(right), Is.True);
    }

    [Test]
    public void DiagnosticFormattingDoesNotRevealValues()
    {
        var name = new AssetName("Private example name");
        var identity = new AssetId(Guid.NewGuid());

        using (Assert.EnterMultipleScope())
        {
            Assert.That($"{name}", Is.EqualTo("AssetName"));
            Assert.That($"{identity}", Is.EqualTo("AssetId"));
        }
    }
}
