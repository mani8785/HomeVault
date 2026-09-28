namespace HomeVault.Domain.Vaults;

/// <summary>The lifecycle state of a Vault.</summary>
public enum VaultStatus
{
    /// <summary>The Vault is active; operations still require applicable authorization.</summary>
    Active,
    /// <summary>The Vault is archived; mutation operations are prohibited.</summary>
    Archived
}
