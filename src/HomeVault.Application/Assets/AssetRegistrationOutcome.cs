namespace HomeVault.Application.Assets;

/// <summary>Safe outcomes of atomic Asset registration.</summary>
public enum AssetRegistrationOutcome
{
    /// <summary>The new snapshot was accepted.</summary>
    Added,
    /// <summary>The Vault is missing or inaccessible to the actor.</summary>
    VaultUnavailable,
    /// <summary>The actor is a member without registration permission.</summary>
    Forbidden,
    /// <summary>The actor can write, but the Vault is archived.</summary>
    VaultArchived,
    /// <summary>The Asset identity already exists; existing data is unchanged.</summary>
    IdentityConflict
}
