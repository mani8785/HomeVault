using HomeVault.Domain.Vaults;

namespace HomeVault.Infrastructure;

/// <summary>Shares process-local Vault and Asset snapshots between explicitly composed adapters.</summary>
/// <remarks>Instance lifetime defines storage lifetime. No public read or mutation API is exposed.</remarks>
public sealed class InMemoryHomeVaultStore
{
    internal object Gate { get; } = new();
    internal Dictionary<Guid, StoredVault> Vaults { get; } = new();
    internal Dictionary<Guid, StoredAsset> Assets { get; } = new();

    internal void SeedVault(Vault vault)
    {
        lock (Gate)
        {
            Vaults[vault.Id] = Snapshot(vault);
        }
    }

    internal static StoredVault Snapshot(Vault vault) => new(vault.Id, vault.Name, vault.Type, vault.Status,
        new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, VaultRole>(vault.Memberships.ToDictionary(member => member.ActorId, member => member.Role)));

    internal sealed record StoredVault(Guid Id, string Name, VaultType Type, VaultStatus Status, IReadOnlyDictionary<Guid, VaultRole> Members)
    {
        internal Guid OwnerId => Members.Single(member => member.Value == VaultRole.Owner).Key;
        public override string ToString() => nameof(StoredVault);
    }

    internal sealed record StoredAsset(Guid Id, Guid VaultId, string Name)
    {
        public override string ToString() => nameof(StoredAsset);
    }
}
