using HomeVault.Application.Identity;
using HomeVault.Domain.Relationships;

namespace HomeVault.Application.Relationships;

/// <summary>Coordinates directed Relationships using one trusted identity read and atomic storage operations.</summary>
public sealed class RelationshipUseCases
{
    private readonly ICurrentActor _actor;
    private readonly IRelationshipStore _store;
    /// <summary>Configures collaborators without reading identity or storage.</summary>
    /// <param name="actor">Trusted current actor.</param><param name="store">Atomic access/storage boundary.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public RelationshipUseCases(ICurrentActor actor, IRelationshipStore store)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(store);
        _actor = actor;
        _store = store;
    }
    /// <summary>Creates a directed link after actual same-Vault ownership and current access checks.</summary>
    /// <param name="vaultId">Intended Vault.</param><param name="id">Caller-supplied root identity.</param>
    /// <param name="sourceAssetId">Source Asset.</param><param name="targetAssetId">Target Asset.</param><param name="kind">Supported meaning.</param>
    /// <param name="cancellationToken">Cancellation checked before identity access.</param><returns>A safe outcome.</returns>
    public Task<RelationshipOutcome> CreateAsync(Guid vaultId, Guid id, Guid sourceAssetId, Guid targetAssetId, RelationshipKind kind, CancellationToken cancellationToken = default) =>
        Mutate(vaultId, id, sourceAssetId == Guid.Empty || targetAssetId == Guid.Empty,
            actor => _store.CreateAsync(vaultId, actor, id, sourceAssetId, targetAssetId, kind, cancellationToken), cancellationToken);
    /// <summary>Removes the association while retaining its root and both endpoint Assets.</summary>
    /// <param name="vaultId">Owning Vault.</param><param name="id">Root identity.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe outcome, including authorized repeated success.</returns>
    public Task<RelationshipOutcome> RemoveAsync(Guid vaultId, Guid id, CancellationToken cancellationToken = default) =>
        Mutate(vaultId, id, false, actor => _store.RemoveAsync(vaultId, actor, id, cancellationToken), cancellationToken);
    /// <summary>Reads an authorized Active or Removed root without loading Asset contents.</summary>
    /// <param name="vaultId">Owning Vault.</param><param name="id">Root identity.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>Null for absent identity or unavailable Vault/root.</returns>
    public Task<RelationshipMetadata?> FindAsync(Guid vaultId, Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty || vaultId == Guid.Empty || id == Guid.Empty) return Task.FromResult<RelationshipMetadata?>(null);
        return _store.FindAsync(vaultId, actor.Value, id, cancellationToken);
    }
    private async Task<RelationshipOutcome> Mutate(Guid vaultId, Guid id, bool invalidEndpoints, Func<Guid, Task<RelationshipOutcome>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty) return RelationshipOutcome.Unauthenticated;
        if (vaultId == Guid.Empty || id == Guid.Empty || invalidEndpoints) return RelationshipOutcome.InvalidIdentity;
        var outcome = await action(actor.Value);
        return outcome switch
        {
            RelationshipOutcome.Succeeded or RelationshipOutcome.Unavailable or RelationshipOutcome.Forbidden or RelationshipOutcome.Archived or
                RelationshipOutcome.InvalidKind or RelationshipOutcome.SelfReference or RelationshipOutcome.IdentityConflict or RelationshipOutcome.DuplicateRelationship => outcome,
            _ => throw new InvalidOperationException("Unexpected Relationship outcome.")
        };
    }
}
