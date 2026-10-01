using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;

namespace HomeVault.Infrastructure.Vaults;

/// <summary>Stores creation snapshots for the lifetime of its backing in-memory store.</summary>
/// <remarks>Share an explicit store across adapters, or use a private store through the default constructor. Data is not durable; no authentication or public read API is provided.</remarks>
public sealed class InMemoryVaultRepository : IVaultRepository
{
    private readonly InMemoryHomeVaultStore _store;

    /// <summary>Creates a repository with a private empty store.</summary>
    public InMemoryVaultRepository() : this(new InMemoryHomeVaultStore()) { }

    /// <summary>Creates a repository sharing an explicitly supplied store.</summary>
    /// <param name="store">The process-local store shared with other adapters.</param>
    /// <exception cref="ArgumentNullException">The store is null.</exception>
    public InMemoryVaultRepository(InMemoryHomeVaultStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public Task<VaultAddOutcome> AddAsync(Vault vault, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vault);
        cancellationToken.ThrowIfCancellationRequested();
        var memberships = vault.Memberships;
        if (vault.Status != VaultStatus.Active || memberships.Count != 1 || memberships[0].Role != VaultRole.Owner)
        {
            throw new ArgumentException("Expected an Active Vault with one Owner membership.", nameof(vault));
        }

        var snapshot = InMemoryHomeVaultStore.Snapshot(vault);
        lock (_store.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_store.Vaults.TryAdd(vault.Id, snapshot)
                ? VaultAddOutcome.Added
                : VaultAddOutcome.IdentityConflict);
        }
    }

    internal InMemoryHomeVaultStore.StoredVault? Inspect(Guid id)
    {
        lock (_store.Gate)
        {
            return _store.Vaults.GetValueOrDefault(id);
        }
    }
}
