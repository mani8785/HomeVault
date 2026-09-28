namespace HomeVault.Domain.Vaults;

/// <summary>A member's domain role; application authorization enforces its capabilities.</summary>
public enum VaultRole
{
    /// <summary>Owns the Vault and may manage ownership while preserving at least one Owner.</summary>
    Owner,
    /// <summary>May administer Editor and Viewer memberships within the accepted permission rules.</summary>
    Administrator,
    /// <summary>May change permitted records.</summary>
    Editor,
    /// <summary>May read permitted records.</summary>
    Viewer
}
