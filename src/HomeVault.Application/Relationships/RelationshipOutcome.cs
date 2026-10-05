namespace HomeVault.Application.Relationships;

/// <summary>Safe mutation outcomes without supplied references.</summary>
public enum RelationshipOutcome
{
    /// <summary>The mutation committed or authorized removal was already complete.</summary>
    Succeeded,
    /// <summary>No trusted current actor exists.</summary>
    Unauthenticated,
    /// <summary>An identity is empty.</summary>
    InvalidIdentity,
    /// <summary>The Vault, root or endpoint is absent/inaccessible or belongs to another Vault.</summary>
    Unavailable,
    /// <summary>The current role cannot write.</summary>
    Forbidden,
    /// <summary>The owning Vault is archived.</summary>
    Archived,
    /// <summary>The kind is unsupported.</summary>
    InvalidKind,
    /// <summary>The source and target are the same Asset.</summary>
    SelfReference,
    /// <summary>The identity is already reserved in this Vault, even by a Removed root.</summary>
    IdentityConflict,
    /// <summary>An active source/target/kind tuple already exists in this Vault.</summary>
    DuplicateRelationship
}
