namespace HomeVault.Application.Assets;

/// <summary>Safe failures from Asset registration.</summary>
public enum RegisterAssetError
{
    /// <summary>Registration succeeded.</summary>
    None,
    /// <summary>No non-empty trusted actor identity is available.</summary>
    Unauthenticated,
    /// <summary>The Asset identity is empty.</summary>
    EmptyIdentity,
    /// <summary>The Vault identity is empty.</summary>
    EmptyVaultIdentity,
    /// <summary>The name is null, empty, or whitespace.</summary>
    BlankName,
    /// <summary>The target Vault is missing or inaccessible.</summary>
    VaultUnavailable,
    /// <summary>The member cannot register Assets.</summary>
    Forbidden,
    /// <summary>The target Vault is archived.</summary>
    VaultArchived,
    /// <summary>The Asset identity already exists.</summary>
    IdentityConflict
}
