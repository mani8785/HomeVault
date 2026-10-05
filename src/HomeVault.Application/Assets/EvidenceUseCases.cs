using HomeVault.Application.Identity;
using HomeVault.Domain.Assets;

namespace HomeVault.Application.Assets;

/// <summary>Coordinates Evidence operations using trusted identity, with no generic aggregate save.</summary>
public sealed class EvidenceUseCases
{
    private readonly ICurrentActor _actor;
    private readonly IEvidenceStore _store;
    /// <summary>Constructs operations without reading identity or storage.</summary>
    /// <param name="actor">Trusted identity read once per call.</param><param name="store">Atomic access/storage boundary.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public EvidenceUseCases(ICurrentActor actor, IEvidenceStore store)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(store);
        _actor = actor;
        _store = store;
    }
    /// <summary>Adds a caller-identified Evidence entry after atomic current-access checks.</summary>
    /// <param name="assetId">Owning Asset.</param><param name="id">Asset-local Evidence identity.</param>
    /// <param name="label">Original label.</param><param name="kind">Url or Note.</param><param name="content">Original text; never logged.</param>
    /// <param name="cancellationToken">Cancellation checked before identity access.</param><returns>A safe mutation outcome.</returns>
    public Task<EvidenceOutcome> AddAsync(Guid assetId, Guid id, string? label, EvidenceKind kind, string? content, CancellationToken cancellationToken = default) =>
        Mutate(assetId, id, actor => _store.AddAsync(assetId, actor, id, label, kind, content, cancellationToken), cancellationToken);
    /// <summary>Removes a stored entry without deleting its referenced resource.</summary>
    /// <param name="assetId">Owning Asset.</param><param name="id">Evidence identity.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe outcome.</returns>
    public Task<EvidenceOutcome> RemoveAsync(Guid assetId, Guid id, CancellationToken cancellationToken = default) =>
        Mutate(assetId, id, actor => _store.RemoveAsync(assetId, actor, id, cancellationToken), cancellationToken);
    /// <summary>Reads metadata only, never content.</summary>
    /// <param name="assetId">Owning Asset.</param><param name="cancellationToken">Operation cancellation.</param>
    /// <returns>Null for absent identity or unavailable Asset, otherwise an immutable snapshot.</returns>
    public async Task<IReadOnlyList<EvidenceMetadata>?> ListAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty || assetId == Guid.Empty) return null;
        var entries = await _store.ListAsync(assetId, actor.Value, cancellationToken);
        return entries is null ? null : Array.AsReadOnly(entries.ToArray());
    }
    /// <summary>Deliberately reads one authorized content snapshot.</summary>
    /// <param name="assetId">Owning Asset.</param><param name="id">Evidence identity.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>Null for absent identity or unavailable entry/Asset.</returns>
    public Task<EvidenceContent?> ReadContentAsync(Guid assetId, Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty || assetId == Guid.Empty || id == Guid.Empty) return Task.FromResult<EvidenceContent?>(null);
        return _store.ReadContentAsync(assetId, actor.Value, id, cancellationToken);
    }
    private async Task<EvidenceOutcome> Mutate(Guid assetId, Guid id, Func<Guid, Task<EvidenceOutcome>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty) return EvidenceOutcome.Unauthenticated;
        if (assetId == Guid.Empty || id == Guid.Empty) return EvidenceOutcome.InvalidIdentity;
        var outcome = await action(actor.Value);
        return outcome switch
        {
            EvidenceOutcome.Succeeded or EvidenceOutcome.Unavailable or EvidenceOutcome.Forbidden or EvidenceOutcome.Archived or
                EvidenceOutcome.BlankLabel or EvidenceOutcome.InvalidKind or EvidenceOutcome.BlankContent or EvidenceOutcome.InvalidUrl or EvidenceOutcome.DuplicateIdentity => outcome,
            _ => throw new InvalidOperationException("Unexpected Evidence outcome.")
        };
    }
}
