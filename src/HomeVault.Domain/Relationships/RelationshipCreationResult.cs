namespace HomeVault.Domain.Relationships;

/// <summary>Contains a valid Relationship or a safe creation failure.</summary>
public sealed class RelationshipCreationResult
{
    internal RelationshipCreationResult(Relationship relationship) => Relationship = relationship;

    internal RelationshipCreationResult(RelationshipCreationError error) => Error = error;

    /// <summary>Gets whether creation succeeded.</summary>
    public bool IsSuccess => Relationship is not null;

    /// <summary>Gets the Relationship on success, otherwise null.</summary>
    public Relationship? Relationship { get; }

    /// <summary>Gets the failure code, or None on success.</summary>
    public RelationshipCreationError Error { get; }

    /// <summary>Returns a safe label without formatting identities.</summary>
    /// <returns>The label RelationshipCreationResult.</returns>
    public override string ToString() => nameof(RelationshipCreationResult);
}
