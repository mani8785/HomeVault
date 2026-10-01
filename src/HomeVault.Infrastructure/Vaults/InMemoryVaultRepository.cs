using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;

namespace HomeVault.Infrastructure.Vaults;

/// <summary>Stores creation snapshots for the lifetime of this repository instance.</summary>
/// <remarks>Share an instance to share its store. Data is not durable; no authentication or public read API is provided.</remarks>
public sealed class InMemoryVaultRepository : IVaultRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, StoredVault> _vaults = new();

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

        var snapshot = new StoredVault(vault.Id, vault.Name, vault.Type, vault.Status, memberships[0].ActorId);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_vaults.TryAdd(vault.Id, snapshot)
                ? VaultAddOutcome.Added
                : VaultAddOutcome.IdentityConflict);
        }
    }

    internal StoredVault? Inspect(Guid id)
    {
        lock (_gate)
        {
            return _vaults.GetValueOrDefault(id);
        }
    }

    internal sealed record StoredVault(Guid Id, string Name, VaultType Type, VaultStatus Status, Guid OwnerId)
    {
        public override string ToString() => nameof(StoredVault);
    }
}
