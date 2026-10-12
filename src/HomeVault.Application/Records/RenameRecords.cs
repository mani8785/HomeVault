using HomeVault.Application.Identity;

namespace HomeVault.Application.Records;

/// <summary>Coordinates record renaming with a trusted actor and an atomic storage boundary.</summary>
public sealed class RenameRecords
{
    private readonly ICurrentActor _actor;
    private readonly IRecordNameStore _store;

    /// <summary>Constructs operations without accessing storage or identity.</summary>
    /// <param name="actor">Trusted current actor, read once per call.</param><param name="store">Atomic name storage.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public RenameRecords(ICurrentActor actor, IRecordNameStore store)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(store);
        _actor = actor;
        _store = store;
    }

    /// <summary>Requests a Vault rename without accepting a caller-supplied actor.</summary>
    /// <param name="vaultId">Target Vault.</param><param name="name">Replacement name.</param><param name="expectedName">Exact last-read name.</param>
    /// <param name="cancellationToken">Cancellation checked before identity access.</param><returns>A safe rename outcome.</returns>
    public Task<RenameOutcome> VaultAsync(Guid vaultId, string? name, string? expectedName, CancellationToken cancellationToken = default) =>
        Execute(vaultId, actor => _store.RenameVaultAsync(vaultId, actor, name, expectedName, cancellationToken), cancellationToken);

    /// <summary>Requests an Asset rename within its actual owning Vault.</summary>
    /// <param name="assetId">Target Asset.</param><param name="name">Replacement name.</param><param name="expectedName">Exact last-read name.</param>
    /// <param name="cancellationToken">Cancellation checked before identity access.</param><returns>A safe rename outcome.</returns>
    public Task<RenameOutcome> AssetAsync(Guid assetId, string? name, string? expectedName, CancellationToken cancellationToken = default) =>
        Execute(assetId, actor => _store.RenameAssetAsync(assetId, actor, name, expectedName, cancellationToken), cancellationToken);

    private async Task<RenameOutcome> Execute(Guid id, Func<Guid, Task<RenameOutcome>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty) return RenameOutcome.Unauthenticated;
        if (id == Guid.Empty) return RenameOutcome.InvalidIdentity;
        var outcome = await action(actor.Value);
        return outcome switch
        {
            RenameOutcome.Succeeded or RenameOutcome.Unavailable or RenameOutcome.Forbidden or RenameOutcome.Archived or
                RenameOutcome.InvalidName or RenameOutcome.InvalidExpectedName or RenameOutcome.Conflict => outcome,
            _ => throw new InvalidOperationException("Unexpected rename outcome.")
        };
    }
}
