namespace HomeVault.Domain.Vaults;

/// <summary>Describes a Vault's ownership context, without changing its permission rules.</summary>
public enum VaultType
{
    /// <summary>A personal context; does not impose a one-member limit.</summary>
    Personal,
    /// <summary>A shared household context.</summary>
    Household,
    /// <summary>An organization context.</summary>
    Organization
}
