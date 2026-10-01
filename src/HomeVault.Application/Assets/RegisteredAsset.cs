using HomeVault.Domain.Assets;

namespace HomeVault.Application.Assets;

/// <summary>Immutable metadata of an Asset accepted by storage.</summary>
public sealed class RegisteredAsset
{
    internal RegisteredAsset(Asset asset)
    {
        Id = asset.Id;
        VaultId = asset.VaultId!.Value;
        Name = asset.Name;
    }

    /// <summary>Gets the Asset identity.</summary>
    public Guid Id { get; }
    /// <summary>Gets the owning Vault identity.</summary>
    public Guid VaultId { get; }
    /// <summary>Gets the original private name for deliberate reads.</summary>
    public string Name { get; }
    /// <summary>Returns a label without private metadata.</summary>
    /// <returns>The type label.</returns>
    public override string ToString() => nameof(RegisteredAsset);
}
