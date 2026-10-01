namespace HomeVault.Application.Vaults;

/// <summary>Contains an immutable created-Vault view or a safe application error.</summary>
/// <remarks>Success means repository acceptance; durability depends on the adapter.</remarks>
public sealed class CreateVaultResult
{
    internal CreateVaultResult(CreatedVault vault) => Vault = vault;

    internal CreateVaultResult(CreateVaultError error) => Error = error;

    /// <summary>Gets whether the repository accepted the new Vault.</summary>
    public bool IsSuccess => Vault is not null;

    /// <summary>Gets the immutable view on success, or null on failure.</summary>
    public CreatedVault? Vault { get; }

    /// <summary>Gets the error, or None on success.</summary>
    public CreateVaultError Error { get; }

    /// <summary>Returns a safe label without formatting the result's metadata.</summary>
    /// <returns>The label CreateVaultResult.</returns>
    public override string ToString() => nameof(CreateVaultResult);
}
