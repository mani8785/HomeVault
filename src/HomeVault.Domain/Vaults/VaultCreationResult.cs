namespace HomeVault.Domain.Vaults;

/// <summary>Contains either a created Vault or a safe validation error.</summary>
public sealed class VaultCreationResult
{
    internal VaultCreationResult(Vault vault) => Vault = vault;

    internal VaultCreationResult(VaultCreationError error) => Error = error;

    /// <summary>Gets whether a valid Vault was created.</summary>
    public bool IsSuccess => Vault is not null;

    /// <summary>Gets the created Vault, or null on failure.</summary>
    public Vault? Vault { get; }

    /// <summary>Gets the failure code, or None on success.</summary>
    public VaultCreationError Error { get; }

    /// <summary>Returns a label without formatting caller-supplied data.</summary>
    /// <returns>The label VaultCreationResult.</returns>
    public override string ToString() => nameof(VaultCreationResult);
}
