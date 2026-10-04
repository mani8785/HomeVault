using HomeVault.Application.Identity;

namespace HomeVault.Application.Reminders;

/// <summary>Coordinates Reminder operations using the trusted actor exactly once per invocation.</summary>
public sealed class ReminderUseCases
{
    private readonly ICurrentActor _actor;
    private readonly IReminderStore _store;
    /// <summary>Configures identity and atomic persistence collaborators without reading them.</summary>
    /// <param name="actor">Trusted identity.</param><param name="store">Atomic storage boundary.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public ReminderUseCases(ICurrentActor actor, IReminderStore store)
    {
        ArgumentNullException.ThrowIfNull(actor); ArgumentNullException.ThrowIfNull(store);
        _actor = actor; _store = store;
    }
    /// <summary>Creates Pending state using actual same-Vault ownership and current writer checks.</summary>
    /// <param name="vaultId">Route Vault.</param><param name="id">Caller root identity.</param><param name="assetId">Referenced Asset.</param>
    /// <param name="action">Private action.</param><param name="dueAt">Exact instant.</param><param name="token">Cancellation.</param>
    /// <returns>A safe outcome.</returns>
    public Task<ReminderOutcome> CreateAsync(Guid vaultId, Guid id, Guid assetId, string? action, DateTimeOffset dueAt, CancellationToken token = default) =>
        Mutate(vaultId, id, assetId == Guid.Empty, actor => _store.CreateAsync(vaultId, actor, id, assetId, action, dueAt, token), token);
    /// <summary>Atomically replaces both Pending fields.</summary>
    /// <param name="vaultId">Route Vault.</param><param name="id">Root identity.</param><param name="action">Private replacement.</param>
    /// <param name="dueAt">Replacement instant.</param><param name="token">Cancellation.</param><returns>A safe outcome.</returns>
    public Task<ReminderOutcome> UpdateAsync(Guid vaultId, Guid id, string? action, DateTimeOffset dueAt, CancellationToken token = default) =>
        Mutate(vaultId, id, false, actor => _store.UpdateAsync(vaultId, actor, id, action, dueAt, token), token);
    /// <summary>Completes a root with authorized same-terminal repeat semantics.</summary>
    /// <param name="vaultId">Route Vault.</param><param name="id">Root identity.</param><param name="token">Cancellation.</param><returns>A safe outcome.</returns>
    public Task<ReminderOutcome> CompleteAsync(Guid vaultId, Guid id, CancellationToken token = default) =>
        Mutate(vaultId, id, false, actor => _store.CompleteAsync(vaultId, actor, id, token), token);
    /// <summary>Cancels a root with authorized same-terminal repeat semantics.</summary>
    /// <param name="vaultId">Route Vault.</param><param name="id">Root identity.</param><param name="token">Cancellation.</param><returns>A safe outcome.</returns>
    public Task<ReminderOutcome> CancelAsync(Guid vaultId, Guid id, CancellationToken token = default) =>
        Mutate(vaultId, id, false, actor => _store.CancelAsync(vaultId, actor, id, token), token);
    /// <summary>Reads authorized state, including terminal roots and Archived Vaults.</summary>
    /// <param name="vaultId">Route Vault.</param><param name="id">Root identity.</param><param name="token">Cancellation.</param>
    /// <returns>Null when identity or the resource is unavailable.</returns>
    public Task<ReminderSnapshot?> FindAsync(Guid vaultId, Guid id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); var actor = _actor.ActorId;
        return actor is null || actor == Guid.Empty || vaultId == Guid.Empty || id == Guid.Empty
            ? Task.FromResult<ReminderSnapshot?>(null) : _store.FindAsync(vaultId, actor.Value, id, token);
    }
    private async Task<ReminderOutcome> Mutate(Guid vaultId, Guid id, bool invalidAsset, Func<Guid, Task<ReminderOutcome>> write, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty) return ReminderOutcome.Unauthenticated;
        if (vaultId == Guid.Empty || id == Guid.Empty || invalidAsset) return ReminderOutcome.InvalidIdentity;
        var outcome = await write(actor.Value);
        return outcome switch
        {
            ReminderOutcome.Succeeded or ReminderOutcome.Unavailable or ReminderOutcome.Forbidden or ReminderOutcome.Archived or
            ReminderOutcome.BlankAction or ReminderOutcome.IdentityConflict or ReminderOutcome.NotPending => outcome,
            _ => throw new InvalidOperationException("Unexpected Reminder outcome.")
        };
    }
}
