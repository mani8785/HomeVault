namespace HomeVault.Domain.Vaults;

/// <summary>Safe outcomes of membership mutations without actor identities or other supplied data.</summary>
public enum VaultMembershipError
{
    /// <summary>The mutation succeeded.</summary>
    None,
    /// <summary>The actor identity is empty.</summary>
    EmptyActorIdentity,
    /// <summary>The role is unsupported.</summary>
    InvalidRole,
    /// <summary>The actor already has a membership in this Vault.</summary>
    DuplicateMember,
    /// <summary>The actor has no membership in this Vault.</summary>
    MemberNotFound,
    /// <summary>The mutation would remove or demote the final Owner.</summary>
    LastOwner
}
