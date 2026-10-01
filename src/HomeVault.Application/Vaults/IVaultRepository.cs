using HomeVault.Domain.Vaults;

namespace HomeVault.Application.Vaults;

/// <summary>Defines the storage boundary for inserting a newly created Vault.</summary>
/// <remarks>
/// Application owns this contract; Infrastructure supplies adapters. It provides no
/// lookup, update, authentication, or authorization operations. Implementations must
/// preserve the same insertion guarantees independently of the storage technology.
/// </remarks>
public interface IVaultRepository
{
    /// <summary>Atomically inserts a Vault and its sole initial Owner without replacing an existing identity.</summary>
    /// <param name="vault">A domain-created Active Vault with exactly one membership whose role is Owner.</param>
    /// <param name="cancellationToken">Cancellation for the storage operation; already-requested cancellation must prevent a write.</param>
    /// <returns>
    /// Added when the adapter accepts the complete insertion, or IdentityConflict when
    /// the identity already exists anywhere in the repository, regardless of ownership.
    /// A conflict leaves existing data unchanged and exposes no existing metadata.
    /// </returns>
    /// <exception cref="ArgumentNullException">The supplied Vault is null.</exception>
    /// <exception cref="ArgumentException">The Vault is not Active with exactly one Owner membership.</exception>
    /// <exception cref="OperationCanceledException">Cancellation prevents completion of the operation.</exception>
    /// <remarks>
    /// Application must bind the initial Owner to its trusted current actor before calling.
    /// Concurrent insertions of the same identity must permit only one Added outcome;
    /// even an identical repeat is a conflict, not an idempotent success. Implementations
    /// store an independent snapshot so later mutations of the supplied instance cannot
    /// change stored data. Callers must not mutate the instance while this call runs.
    /// Added does not promise durable storage. Cancellation or an unexpected storage
    /// exception after work begins does not prove that no commit occurred; unexpected
    /// exceptions propagate and must not be logged with sensitive values. There is no
    /// automatic retry. Missing-record reads and existing-record concurrency policies
    /// require separate contracts when their application scenarios are introduced.
    /// </remarks>
    Task<VaultAddOutcome> AddAsync(Vault vault, CancellationToken cancellationToken);
}
