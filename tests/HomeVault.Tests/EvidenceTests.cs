using System.Text.Json;
using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class EvidenceTests
{
    private static Asset CreateAsset() => Asset.Create(Guid.NewGuid(), "Example").Asset!;

    [TestCase(EvidenceKind.Url, "https://example.invalid/receipt?q=fictional#page")]
    [TestCase(EvidenceKind.Url, "HTTP://example.invalid:8080/a%20b")]
    [TestCase(EvidenceKind.Note, "  Fictional note – København\nSecond line  ")]
    public void AddPreservesIdentityLabelKindAndContent(EvidenceKind kind, string content)
    {
        var asset = CreateAsset();
        var id = Guid.NewGuid();
        Assert.That(asset.AddEvidence(id, " Receipt ", kind, content), Is.EqualTo(EvidenceError.None));
        var entry = asset.Evidence.Single();
        Assert.That(entry.Id, Is.EqualTo(id));
        Assert.That(entry.Label, Is.EqualTo(" Receipt "));
        Assert.That(entry.Kind, Is.EqualTo(kind));
        Assert.That(entry.ReadContent(), Is.EqualTo(content));
    }

    [TestCase("/relative")]
    [TestCase("file:///C:/receipt.pdf")]
    [TestCase("ftp://example.invalid/file")]
    [TestCase("https://")]
    [TestCase("https://user:pass@example.invalid/file")]
    [TestCase("https://user@example.invalid/file")]
    [TestCase(" https://example.invalid")]
    [TestCase("https://example.invalid/a b")]
    [TestCase("https://example.invalid/\tfile")]
    [TestCase("https://example.invalid/\u0001file")]
    [TestCase("https://example.invalid/\u2003file")]
    public void InvalidUrlsFailWithoutAddingEvidence(string content)
    {
        var asset = CreateAsset();
        Assert.That(asset.AddEvidence(Guid.NewGuid(), "Receipt", EvidenceKind.Url, content), Is.EqualTo(EvidenceError.InvalidUrl));
        Assert.That(asset.Evidence, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    [TestCase("\u2003")]
    public void BlankLabelsAndContentAreRejectedInOrder(string? blank)
    {
        var asset = CreateAsset();
        Assert.That(asset.AddEvidence(Guid.NewGuid(), blank, (EvidenceKind)99, null), Is.EqualTo(EvidenceError.BlankLabel));
        Assert.That(asset.AddEvidence(Guid.NewGuid(), "Label", EvidenceKind.Note, blank), Is.EqualTo(EvidenceError.BlankContent));
        Assert.That(asset.AddEvidence(Guid.NewGuid(), "Label", EvidenceKind.Url, blank), Is.EqualTo(EvidenceError.BlankContent));
        Assert.That(asset.Evidence, Is.Empty);
    }

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(int.MaxValue)]
    public void InvalidIdentityAndKindHaveDefinedPrecedence(int invalidKind)
    {
        var asset = CreateAsset();
        Assert.That(asset.AddEvidence(Guid.Empty, null, (EvidenceKind)invalidKind, null), Is.EqualTo(EvidenceError.EmptyIdentity));
        Assert.That(asset.AddEvidence(Guid.NewGuid(), "Label", (EvidenceKind)invalidKind, null), Is.EqualTo(EvidenceError.InvalidKind));
        Assert.That(asset.RemoveEvidence(Guid.Empty), Is.EqualTo(EvidenceError.EmptyIdentity));
        Assert.That(asset.Evidence, Is.Empty);
    }

    [Test]
    public void DuplicateIdentityAndFailedRemovalsPreserveExistingEntries()
    {
        var asset = CreateAsset();
        var id = Guid.NewGuid();
        asset.AddEvidence(id, "Label", EvidenceKind.Note, "Original");
        var before = asset.Evidence;
        Assert.That(asset.AddEvidence(id, "Replacement", EvidenceKind.Url, "invalid"), Is.EqualTo(EvidenceError.InvalidUrl));
        Assert.That(asset.AddEvidence(id, "Replacement", EvidenceKind.Note, "Changed"), Is.EqualTo(EvidenceError.DuplicateIdentity));
        Assert.That(asset.RemoveEvidence(Guid.NewGuid()), Is.EqualTo(EvidenceError.NotFound));
        Assert.That(asset.RemoveEvidence(Guid.Empty), Is.EqualTo(EvidenceError.EmptyIdentity));
        Assert.That(asset.Evidence, Is.EquivalentTo(before));
        Assert.That(asset.Evidence.Single().ReadContent(), Is.EqualTo("Original"));
    }

    [Test]
    public void RepeatedContentIsAllowedAndRemovalUsesIdentityWithStableSnapshots()
    {
        var asset = CreateAsset();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var emptySnapshot = asset.Evidence;
        asset.AddEvidence(first, "Label", EvidenceKind.Note, "Same content");
        Assert.That(asset.AddEvidence(second, "Label", EvidenceKind.Note, "Same content"), Is.EqualTo(EvidenceError.None));
        var snapshot = asset.Evidence;
        Assert.Throws<NotSupportedException>(() => ((IList<Evidence>)snapshot).Clear());
        Assert.That(asset.RemoveEvidence(first), Is.EqualTo(EvidenceError.None));
        Assert.That(asset.RemoveEvidence(first), Is.EqualTo(EvidenceError.NotFound));
        Assert.That(asset.Evidence.Single().Id, Is.EqualTo(second));
        Assert.That(snapshot.Count, Is.EqualTo(2));
        Assert.That(snapshot.Single(entry => entry.Id == first).ReadContent(), Is.EqualTo("Same content"));
        Assert.That(emptySnapshot, Is.Empty);
    }

    [Test]
    public void EvidenceIdentityIsScopedToOwningAsset()
    {
        var first = CreateAsset();
        var second = CreateAsset();
        var id = Guid.NewGuid();
        first.AddEvidence(id, "Label", EvidenceKind.Note, "First");
        Assert.That(second.AddEvidence(id, "Label", EvidenceKind.Note, "Second"), Is.EqualTo(EvidenceError.None));
        first.RemoveEvidence(id);
        Assert.That(second.Evidence.Single().ReadContent(), Is.EqualTo("Second"));
    }

    [TestCase(EvidenceKind.Note, "fictional-private-note-sentinel")]
    [TestCase(EvidenceKind.Url, "https://example.invalid/?token=fictional-private-sentinel")]
    public void DiagnosticsAndDefaultJsonOmitContent(EvidenceKind kind, string content)
    {
        var asset = CreateAsset();
        var id = Guid.NewGuid();
        asset.AddEvidence(id, "Label", kind, content);
        var entry = asset.Evidence.Single();
        Assert.That(entry.ToString(), Is.EqualTo("Evidence"));
        Assert.That(JsonSerializer.Serialize(entry), Does.Not.Contain("fictional-private"));
        Assert.That(JsonSerializer.Serialize(asset), Does.Not.Contain("fictional-private"));
        Assert.That(asset.AddEvidence(id, "Label", kind, content).ToString(), Is.EqualTo("DuplicateIdentity"));
        Assert.That(entry.ReadContent(), Is.EqualTo(content));
    }
}
