using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;

namespace HomeVault.Infrastructure.Assets;

/// <summary>Checks stored Vault state and inserts Asset snapshots under the shared store lock.</summary>
public sealed class InMemoryAssetRegistrationStore : IAssetRegistrationStore
{
    private readonly InMemoryHomeVaultStore _store;

    /// <summary>Constructs the adapter for an explicitly shared store.</summary>
    /// <param name="store">The same store used by the Vault repository.</param>
    /// <exception cref="ArgumentNullException">The store is null.</exception>
    public InMemoryAssetRegistrationStore(InMemoryHomeVaultStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public Task<AssetRegistrationOutcome> RegisterAsync(Asset asset, Guid actorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        cancellationToken.ThrowIfCancellationRequested();
        if (actorId == Guid.Empty)
            throw new ArgumentException("A non-empty actor identity is required.", nameof(actorId));
        if (asset.VaultId is not { } vaultId || asset.Attributes.Count != 0 || asset.Evidence.Count != 0)
            throw new ArgumentException("Expected a Vault-bound Asset without attributes or Evidence.", nameof(asset));
        var snapshot = new InMemoryHomeVaultStore.StoredAsset(asset.Id, vaultId, asset.Name);
        lock (_store.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_store.Vaults.TryGetValue(vaultId, out var vault) || !vault.Members.TryGetValue(actorId, out var role))
                return Task.FromResult(AssetRegistrationOutcome.VaultUnavailable);
            if (role is not (VaultRole.Owner or VaultRole.Administrator or VaultRole.Editor))
                return Task.FromResult(AssetRegistrationOutcome.Forbidden);
            if (vault.Status != VaultStatus.Active)
                return Task.FromResult(AssetRegistrationOutcome.VaultArchived);
            return Task.FromResult(_store.Assets.TryAdd(asset.Id, snapshot)
                ? AssetRegistrationOutcome.Added : AssetRegistrationOutcome.IdentityConflict);
        }
    }

    internal InMemoryHomeVaultStore.StoredAsset? Inspect(Guid id)
    {
        lock (_store.Gate)
            return _store.Assets.GetValueOrDefault(id);
    }
}
