using HomeVault.Application.Identity;

namespace HomeVault.Application.Assets;

/// <summary>Inspects an Asset through the current actor's membership.</summary>
public sealed class InspectAssetUseCase
{
    private readonly ICurrentActor _actor;
    private readonly IAssetInspectionStore _store;

    /// <summary>Constructs the use case with trusted identity and scoped storage.</summary>
    /// <param name="actor">Identity read once on each call.</param>
    /// <param name="store">The membership-scoped read adapter.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public InspectAssetUseCase(ICurrentActor actor, IAssetInspectionStore store)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(store);
        _actor = actor;
        _store = store;
    }

    /// <summary>Reads permitted metadata, returning the same unavailable result for invalid, missing, or inaccessible identities.</summary>
    /// <param name="assetId">The target Asset identity.</param>
    /// <param name="cancellationToken">Cancellation checked before reading identity and passed to storage.</param>
    /// <returns>A permitted immutable view, or null. A missing actor never triggers storage access.</returns>
    /// <exception cref="OperationCanceledException">The operation is cancelled.</exception>
    /// <remarks>Does not authenticate, cache access, log metadata, or retry storage failures.</remarks>
    public async Task<InspectedAsset?> ExecuteAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actorId = _actor.ActorId;
        if (actorId is null || actorId == Guid.Empty || assetId == Guid.Empty) return null;
        return await _store.FindAsync(assetId, actorId.Value, cancellationToken);
    }
}
