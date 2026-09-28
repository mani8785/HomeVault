namespace HomeVault.Domain.Relationships;

/// <summary>Safe creation outcomes without supplied identities.</summary>
public enum RelationshipCreationError
{
    /// <summary>Creation succeeded.</summary>
    None,
    /// <summary>The Relationship identity is empty.</summary>
    EmptyIdentity,
    /// <summary>The Vault identity is empty.</summary>
    EmptyVaultIdentity,
    /// <summary>The source Asset identity is empty.</summary>
    EmptySourceIdentity,
    /// <summary>The target Asset identity is empty.</summary>
    EmptyTargetIdentity,
    /// <summary>The kind is unsupported.</summary>
    InvalidKind,
    /// <summary>The source and target reference the same Asset.</summary>
    SelfReference
}
