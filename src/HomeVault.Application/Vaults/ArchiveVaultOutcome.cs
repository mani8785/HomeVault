namespace HomeVault.Application.Vaults;

/// <summary>Safe outcomes of archival; no private metadata is included.</summary>
public enum ArchiveVaultOutcome
{
    /// <summary>The Vault is durably Archived, including an authorized repeat.</summary>
    Archived,
    /// <summary>No trusted actor is available.</summary>
    Unauthenticated,
    /// <summary>The target identity is empty.</summary>
    InvalidIdentity,
    /// <summary>The Vault is missing or the actor is not a member.</summary>
    Unavailable,
    /// <summary>The current member is not an Owner.</summary>
    Forbidden
}
