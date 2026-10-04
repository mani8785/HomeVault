namespace HomeVault.Application.Reminders;

/// <summary>Atomic current-access boundary for durable Reminder operations; actual Asset ownership is verified inside each transaction.</summary>
public interface IReminderStore
{
    /// <summary>Creates Pending state after writer, archive, Asset ownership, action and identity checks.</summary>
    /// <param name="vaultId">Route Vault.</param><param name="actorId">Trusted actor.</param><param name="id">New root identity.</param>
    /// <param name="assetId">Actual same-Vault Asset.</param><param name="action">Private action.</param><param name="dueAt">Exact instant.</param>
    /// <param name="token">Cancellation.</param><returns>A safe outcome.</returns>
    Task<ReminderOutcome> CreateAsync(Guid vaultId, Guid actorId, Guid id, Guid assetId, string? action, DateTimeOffset dueAt, CancellationToken token);
    /// <summary>Replaces both Pending fields atomically; terminal state precedes action validation.</summary>
    /// <param name="vaultId">Route Vault.</param><param name="actorId">Trusted actor.</param><param name="id">Root identity.</param>
    /// <param name="action">Replacement private action.</param><param name="dueAt">Replacement instant.</param>
    /// <param name="token">Cancellation.</param><returns>A safe outcome.</returns>
    Task<ReminderOutcome> UpdateAsync(Guid vaultId, Guid actorId, Guid id, string? action, DateTimeOffset dueAt, CancellationToken token);
    /// <summary>Completes a Pending root or succeeds unchanged for an authorized Completed repeat.</summary>
    /// <param name="vaultId">Route Vault.</param><param name="actorId">Trusted actor.</param><param name="id">Root identity.</param>
    /// <param name="token">Cancellation.</param><returns>A safe outcome.</returns>
    Task<ReminderOutcome> CompleteAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token);
    /// <summary>Cancels a Pending root or succeeds unchanged for an authorized Cancelled repeat.</summary>
    /// <param name="vaultId">Route Vault.</param><param name="actorId">Trusted actor.</param><param name="id">Root identity.</param>
    /// <param name="token">Cancellation.</param><returns>A safe outcome.</returns>
    Task<ReminderOutcome> CancelAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token);
    /// <summary>Reads current-member scoped state consistently, including Archived Vaults.</summary>
    /// <param name="vaultId">Route Vault.</param><param name="actorId">Trusted actor.</param><param name="id">Root identity.</param>
    /// <param name="token">Cancellation.</param><returns>An authorized snapshot or null for unavailable resources.</returns>
    Task<ReminderSnapshot?> FindAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token);
}
