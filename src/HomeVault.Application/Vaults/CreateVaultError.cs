namespace HomeVault.Application.Vaults;

/// <summary>Safe failures from the create-Vault use case.</summary>
public enum CreateVaultError
{
    /// <summary>The repository accepted the new Vault.</summary>
    None,
    /// <summary>The trusted identity context supplied no non-empty actor identity.</summary>
    Unauthenticated,
    /// <summary>The proposed Vault identity is empty.</summary>
    EmptyIdentity,
    /// <summary>The proposed name is null, empty, or entirely whitespace.</summary>
    BlankName,
    /// <summary>The proposed Vault type is unsupported.</summary>
    InvalidType,
    /// <summary>The Vault identity already exists; no existing metadata is exposed or overwritten.</summary>
    IdentityConflict
}
