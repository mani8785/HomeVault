namespace HomeVault.Application.Assets;

/// <summary>Supplies proposed Asset metadata without nominating an actor.</summary>
/// <param name="id">The proposed Asset identity.</param>
/// <param name="vaultId">The target Vault identity.</param>
/// <param name="name">The proposed name, validated during execution.</param>
public sealed class RegisterAssetRequest(Guid id, Guid vaultId, string? name)
{
    /// <summary>Gets the proposed Asset identity.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the target Vault identity.</summary>
    public Guid VaultId { get; } = vaultId;
    /// <summary>Gets the proposed private name; avoid logging.</summary>
    public string? Name { get; } = name;
    /// <summary>Returns a label without private metadata.</summary>
    /// <returns>The type label.</returns>
    public override string ToString() => nameof(RegisterAssetRequest);
}
