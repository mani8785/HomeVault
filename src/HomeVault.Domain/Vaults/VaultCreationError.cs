namespace HomeVault.Domain.Vaults;

/// <summary>Safe Vault creation outcomes without caller-supplied data.</summary>
public enum VaultCreationError
{
    /// <summary>Creation succeeded.</summary>
    None,
    /// <summary>The Vault identity is empty.</summary>
    EmptyIdentity,
    /// <summary>The name is null, empty, or entirely whitespace.</summary>
    BlankName,
    /// <summary>The supplied Vault type is unsupported.</summary>
    InvalidType,
    /// <summary>The initial owner identity is empty.</summary>
    EmptyOwnerIdentity
}
