using HomeVault.Application.Identity;

namespace HomeVault.Application.Vaults;

/// <summary>Archives a Vault on behalf of the current trusted actor.</summary>
public sealed class ArchiveVaultUseCase
{
    private readonly ICurrentActor _actor;
    private readonly IVaultArchiveStore _store;

    /// <summary>Constructs the operation with a trusted identity source and atomic storage.</summary>
    /// <param name="actor">Identity source read once per execution.</param>
    /// <param name="store">Current-access and durable-write boundary.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public ArchiveVaultUseCase(ICurrentActor actor, IVaultArchiveStore store)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(store);
        _actor = actor;
        _store = store;
    }

    /// <summary>Validates identity and awaits authorized durable archival.</summary>
    /// <param name="vaultId">Target Vault identity.</param>
    /// <param name="cancellationToken">Cancellation checked before reading actor and passed to storage.</param>
    /// <returns>A safe outcome without Vault metadata.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    /// <exception cref="InvalidOperationException">Storage returns an unsupported outcome.</exception>
    /// <remarks>Does not retry or log failures. A storage failure does not prove no commit occurred.</remarks>
    public async Task<ArchiveVaultOutcome> ExecuteAsync(Guid vaultId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actorId = _actor.ActorId;
        if (actorId is null || actorId == Guid.Empty) return ArchiveVaultOutcome.Unauthenticated;
        if (vaultId == Guid.Empty) return ArchiveVaultOutcome.InvalidIdentity;
        return await _store.ArchiveAsync(vaultId, actorId.Value, cancellationToken) switch
        {
            ArchiveVaultOutcome.Archived => ArchiveVaultOutcome.Archived,
            ArchiveVaultOutcome.Unavailable => ArchiveVaultOutcome.Unavailable,
            ArchiveVaultOutcome.Forbidden => ArchiveVaultOutcome.Forbidden,
            _ => throw new InvalidOperationException("Unexpected archive outcome.")
        };
    }
}
