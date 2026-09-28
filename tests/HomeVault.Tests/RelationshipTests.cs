using HomeVault.Domain.Relationships;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class RelationshipTests
{
    [Test]
    public void CreationPreservesDirectedReferencesWithoutLoadingAssets()
    {
        var id = Guid.NewGuid();
        var vault = Guid.NewGuid();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var result = Relationship.Create(id, vault, source, target, RelationshipKind.Covers);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Error, Is.EqualTo(RelationshipCreationError.None));
        var relationship = result.Relationship!;
        Assert.That(relationship.Id, Is.EqualTo(id));
        Assert.That(relationship.VaultId, Is.EqualTo(vault));
        Assert.That(relationship.SourceAssetId, Is.EqualTo(source));
        Assert.That(relationship.TargetAssetId, Is.EqualTo(target));
        Assert.That(relationship.Kind, Is.EqualTo(RelationshipKind.Covers));
        Assert.That(relationship.Status, Is.EqualTo(RelationshipStatus.Active));
    }

    [TestCase(0, RelationshipCreationError.EmptyIdentity)]
    [TestCase(1, RelationshipCreationError.EmptyVaultIdentity)]
    [TestCase(2, RelationshipCreationError.EmptySourceIdentity)]
    [TestCase(3, RelationshipCreationError.EmptyTargetIdentity)]
    public void EmptyIdentitiesFailInOrderBeforeInvalidKind(int firstEmpty, RelationshipCreationError expected)
    {
        var ids = Enumerable.Range(0, 4).Select(index => index < firstEmpty ? Guid.NewGuid() : Guid.Empty).ToArray();
        AssertFailure(Relationship.Create(ids[0], ids[1], ids[2], ids[3], (RelationshipKind)99), expected);
    }

    [TestCase(-1)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public void UnsupportedKindsFailBeforeSelfReference(int kind)
    {
        var endpoint = Guid.NewGuid();
        AssertFailure(Relationship.Create(Guid.NewGuid(), Guid.NewGuid(), endpoint, endpoint, (RelationshipKind)kind), RelationshipCreationError.InvalidKind);
    }

    [Test]
    public void SelfReferenceIsRejected()
    {
        var endpoint = Guid.NewGuid();
        AssertFailure(Relationship.Create(Guid.NewGuid(), Guid.NewGuid(), endpoint, endpoint, RelationshipKind.Covers), RelationshipCreationError.SelfReference);
    }

    [Test]
    public void RemovalRetainsAllReferencesAndRepeatedRemovalIsHarmless()
    {
        var id = Guid.NewGuid();
        var vault = Guid.NewGuid();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var relationship = Relationship.Create(id, vault, source, target, RelationshipKind.Covers).Relationship!;
        relationship.Remove();
        Assert.That(relationship.Status, Is.EqualTo(RelationshipStatus.Removed));
        relationship.Remove();
        Assert.That(relationship.Status, Is.EqualTo(RelationshipStatus.Removed));
        Assert.That(relationship.Id, Is.EqualTo(id));
        Assert.That(relationship.VaultId, Is.EqualTo(vault));
        Assert.That(relationship.SourceAssetId, Is.EqualTo(source));
        Assert.That(relationship.TargetAssetId, Is.EqualTo(target));
        Assert.That(relationship.Kind, Is.EqualTo(RelationshipKind.Covers));
    }

    [Test]
    public void RemovalDoesNotChangeAnotherRelationshipOrReverseItsDirection()
    {
        var vault = Guid.NewGuid();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var forward = Relationship.Create(Guid.NewGuid(), vault, source, target, RelationshipKind.Covers).Relationship!;
        var reverse = Relationship.Create(Guid.NewGuid(), vault, target, source, RelationshipKind.Covers).Relationship!;
        forward.Remove();
        Assert.That(reverse.Status, Is.EqualTo(RelationshipStatus.Active));
        Assert.That(reverse.SourceAssetId, Is.EqualTo(target));
        Assert.That(reverse.TargetAssetId, Is.EqualTo(source));
    }

    [Test]
    public void DiagnosticsContainOnlySafeLabels()
    {
        var result = Relationship.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RelationshipKind.Covers);
        Assert.That(result.ToString(), Is.EqualTo("RelationshipCreationResult"));
        Assert.That(result.Relationship!.ToString(), Is.EqualTo("Relationship"));
        var failed = Relationship.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RelationshipKind.Covers);
        Assert.That(failed.ToString(), Is.EqualTo("RelationshipCreationResult"));
        Assert.That(failed.Error.ToString(), Is.EqualTo("EmptyIdentity"));
    }

    private static void AssertFailure(RelationshipCreationResult result, RelationshipCreationError expected)
    {
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Relationship, Is.Null);
        Assert.That(result.Error, Is.EqualTo(expected));
    }
}
