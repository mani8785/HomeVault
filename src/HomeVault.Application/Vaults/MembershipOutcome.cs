namespace HomeVault.Application.Vaults;

/// <summary>Safe membership operation outcomes, without private account or Vault data.</summary>
public enum MembershipOutcome
{
    /// <summary>The mutation committed, including an authorized unchanged role.</summary>
    Succeeded,
    /// <summary>No trusted requesting actor is available.</summary>
    Unauthenticated,
    /// <summary>A Vault or target identity is empty.</summary>
    InvalidIdentity,
    /// <summary>The requested role is unsupported.</summary>
    InvalidRole,
    /// <summary>The Vault or permitted target is unavailable; no existence details are disclosed.</summary>
    Unavailable,
    /// <summary>The requesting member cannot perform this role transition.</summary>
    Forbidden,
    /// <summary>The Vault is archived and cannot be modified.</summary>
    Archived,
    /// <summary>The target already belongs to the Vault.</summary>
    DuplicateMember,
    /// <summary>The mutation would remove or demote the final Owner.</summary>
    LastOwner
}
