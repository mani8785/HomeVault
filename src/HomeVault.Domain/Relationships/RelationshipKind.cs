namespace HomeVault.Domain.Relationships;

/// <summary>The meaning of a directed association between Asset references.</summary>
public enum RelationshipKind
{
    /// <summary>The source insurance-policy Asset covers the target Asset; endpoint categories are not validated by Domain.</summary>
    Covers
}
