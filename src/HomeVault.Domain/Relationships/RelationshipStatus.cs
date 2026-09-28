namespace HomeVault.Domain.Relationships;

/// <summary>The lifecycle state of an independent Relationship.</summary>
public enum RelationshipStatus
{
    /// <summary>The association is active.</summary>
    Active,
    /// <summary>The association was removed; references remain available for inspection.</summary>
    Removed
}
