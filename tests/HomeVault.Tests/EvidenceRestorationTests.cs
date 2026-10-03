using System.Text.Json;
using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using NUnit.Framework;

namespace HomeVault.Tests;

public sealed class EvidenceRestorationTests
{
    [Test]
    public void RestoresIdentityExactTextAndSnapshotsWithoutSerializingContent()
    {
        var id = Guid.NewGuid(); var vault = Guid.NewGuid(); var evidenceId = Guid.NewGuid();
        var asset = Asset.RestoreEvidence(id, vault, " Item ", [(evidenceId, " Label ", EvidenceKind.Note, " Private sentinel \nمتن ")]);
        var snapshot = asset.Evidence;
        Assert.That(asset.Id, Is.EqualTo(id));
        Assert.That(asset.VaultId, Is.EqualTo(vault));
        Assert.That(asset.Name, Is.EqualTo(" Item "));
        Assert.That(snapshot.Single().Label, Is.EqualTo(" Label "));
        Assert.That(JsonSerializer.Serialize(snapshot), Does.Not.Contain("Private sentinel"));
        Assert.That(asset.RemoveEvidence(evidenceId), Is.EqualTo(EvidenceError.None));
        Assert.That(snapshot.Single().ReadContent(), Is.EqualTo(" Private sentinel \nمتن "));
        var content = new EvidenceContent(snapshot.Single().ReadContent());
        Assert.That(JsonSerializer.Serialize(content), Is.EqualTo("{}"));
        Assert.That(content.ToString(), Is.EqualTo("EvidenceContent"));
        Assert.That(content.ReadContent(), Is.EqualTo(snapshot.Single().ReadContent()));
    }

    [TestCase(" ", 1, "text")]
    [TestCase("Label", 99, "text")]
    [TestCase("Label", 1, " ")]
    [TestCase("Label", 0, "file:///private")]
    public void RejectsInvalidStoredEntriesSafely(string label, int kind, string content)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Asset.RestoreEvidence(Guid.NewGuid(), Guid.NewGuid(), "Private sentinel",
            [(Guid.NewGuid(), label, (EvidenceKind)kind, content)]));
        Assert.That(exception!.Message, Is.EqualTo("Invalid stored Evidence state."));
    }

    [Test]
    public void RejectsEmptyIdentityAndDuplicateEntries()
    {
        var id = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => Asset.RestoreEvidence(Guid.NewGuid(), Guid.NewGuid(), "Item", [(Guid.Empty, "Label", EvidenceKind.Note, "text")]));
        Assert.Throws<InvalidOperationException>(() => Asset.RestoreEvidence(Guid.Empty, Guid.NewGuid(), "Item", []));
        Assert.Throws<InvalidOperationException>(() => Asset.RestoreEvidence(Guid.NewGuid(), Guid.Empty, "Item", []));
        Assert.Throws<InvalidOperationException>(() => Asset.RestoreEvidence(Guid.NewGuid(), Guid.NewGuid(), " ", []));
        Assert.Throws<InvalidOperationException>(() => Asset.RestoreEvidence(Guid.NewGuid(), Guid.NewGuid(), "Item",
            [(id, "Label", EvidenceKind.Note, "first"), (id, "Other", EvidenceKind.Note, "second")]));
    }
}
