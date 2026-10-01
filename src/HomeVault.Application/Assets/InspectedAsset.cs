namespace HomeVault.Application.Assets;

/// <summary>Immutable persisted metadata returned only after membership checks.</summary>
/// <param name="id">Stored Asset identity.</param>
/// <param name="vaultId">Stored owning Vault identity.</param>
/// <param name="name">Stored private name, preserved exactly.</param>
public sealed class InspectedAsset(Guid id, Guid vaultId, string name)
{
    /// <summary>Gets the stored Asset identity.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the owning Vault identity.</summary>
    public Guid VaultId { get; } = vaultId;
    /// <summary>Gets the private name for deliberate reads; do not log it.</summary>
    public string Name { get; } = name;
    /// <summary>Returns a label without private metadata.</summary>
    /// <returns>The type label.</returns>
    public override string ToString() => nameof(InspectedAsset);
}
