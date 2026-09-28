namespace HomeVault.Domain.Vaults;

/// <summary>Safe outcomes of archival without disclosing actor or Vault data.</summary>
public enum VaultArchiveError
{
    /// <summary>The Owner request succeeded, including an already archived Vault.</summary>
    None,
    /// <summary>The requesting actor identity is empty.</summary>
    EmptyActorIdentity,
    /// <summary>The actor is not a current Owner; does not distinguish absence from another role.</summary>
    NotOwner
}
