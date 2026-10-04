using HomeVault.Domain.Relationships;
using NUnit.Framework;

namespace HomeVault.Tests;

public sealed class RelationshipRestorationTests
{
    [TestCase(RelationshipStatus.Active)]
    [TestCase(RelationshipStatus.Removed)]
    public void RestorePreservesReferencesAndLifecycle(RelationshipStatus status)
    {
        var id = Guid.NewGuid(); var vault = Guid.NewGuid(); var source = Guid.NewGuid(); var target = Guid.NewGuid();
        var root = Relationship.Restore(id, vault, source, target, RelationshipKind.Covers, status);
        Assert.That(root.Id, Is.EqualTo(id));
        Assert.That(root.VaultId, Is.EqualTo(vault));
        Assert.That(root.SourceAssetId, Is.EqualTo(source));
        Assert.That(root.TargetAssetId, Is.EqualTo(target));
        Assert.That(root.Status, Is.EqualTo(status));
        root.Remove(); root.Remove();
        Assert.That(root.Status, Is.EqualTo(RelationshipStatus.Removed));
        Assert.That(root.ToString(), Is.EqualTo("Relationship"));
    }
    [TestCase("id")]
    [TestCase("vault")]
    [TestCase("source")]
    [TestCase("target")]
    [TestCase("self")]
    [TestCase("kind")]
    [TestCase("status")]
    public void RestoreRejectsInvalidStoredStateWithoutReferences(string invalid)
    {
        var source = Guid.NewGuid();
        var exception = Assert.Throws<InvalidOperationException>(() => Relationship.Restore(
            invalid == "id" ? Guid.Empty : Guid.NewGuid(), invalid == "vault" ? Guid.Empty : Guid.NewGuid(),
            invalid == "source" ? Guid.Empty : source, invalid == "target" ? Guid.Empty : invalid == "self" ? source : Guid.NewGuid(),
            invalid == "kind" ? (RelationshipKind)99 : RelationshipKind.Covers,
            invalid == "status" ? (RelationshipStatus)99 : RelationshipStatus.Active));
        Assert.That(exception!.Message, Is.EqualTo("Invalid stored Relationship state."));
    }
}
