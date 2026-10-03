using HomeVault.Application.Identity;
using HomeVault.Domain.Assets;

namespace HomeVault.Application.Assets;

/// <summary>Coordinates ordinary attribute operations using trusted identity and an atomic store.</summary>
public sealed class OrdinaryAttributeUseCases
{
    private readonly ICurrentActor _actor;
    private readonly IOrdinaryAttributeStore _store;

    /// <summary>Constructs operations without accessing identity or storage.</summary>
    /// <param name="actor">Trusted identity, read once per call.</param><param name="store">Atomic storage boundary.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public OrdinaryAttributeUseCases(ICurrentActor actor, IOrdinaryAttributeStore store)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(store);
        _actor = actor;
        _store = store;
    }

    /// <summary>Adds an ordinary attribute after atomic access checks.</summary>
    /// <param name="assetId">Target Asset.</param><param name="name">Attribute name.</param><param name="value">Ordinary text.</param>
    /// <param name="sensitivity">Explicit classification, only Ordinary is supported.</param>
    /// <param name="cancellationToken">Cancellation checked before identity access.</param><returns>A safe outcome without supplied data.</returns>
    public Task<AttributeOutcome> AddAsync(Guid assetId, string? name, string? value, AttributeSensitivity sensitivity, CancellationToken cancellationToken = default) =>
        Execute(assetId, actor => _store.AddAsync(assetId, actor, name, value, sensitivity, cancellationToken), cancellationToken);

    /// <summary>Replaces an ordinary value while preserving name spelling and classification.</summary>
    /// <param name="assetId">Target Asset.</param><param name="name">Lookup name.</param><param name="value">Replacement text.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe outcome.</returns>
    public Task<AttributeOutcome> ChangeAsync(Guid assetId, string? name, string? value, CancellationToken cancellationToken = default) =>
        Execute(assetId, actor => _store.ChangeAsync(assetId, actor, name, value, cancellationToken), cancellationToken);

    /// <summary>Removes one ordinary attribute.</summary>
    /// <param name="assetId">Target Asset.</param><param name="name">Lookup name.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe outcome.</returns>
    public Task<AttributeOutcome> RemoveAsync(Guid assetId, string? name, CancellationToken cancellationToken = default) =>
        Execute(assetId, actor => _store.RemoveAsync(assetId, actor, name, cancellationToken), cancellationToken);

    /// <summary>Reads a current-membership-scoped ordinary snapshot; never authorizes Sensitive disclosure.</summary>
    /// <param name="assetId">Target Asset.</param><param name="cancellationToken">Operation cancellation.</param>
    /// <returns>Null for missing identity or absent/inaccessible Asset, otherwise immutable entries.</returns>
    public async Task<IReadOnlyList<AssetAttribute>?> ListAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty || assetId == Guid.Empty) return null;
        var entries = await _store.ListAsync(assetId, actor.Value, cancellationToken);
        if (entries is null) return null;
        if (entries.Any(entry => entry.Sensitivity != AttributeSensitivity.Ordinary))
            throw new InvalidOperationException("Unexpected attribute classification.");
        return Array.AsReadOnly(entries.ToArray());
    }

    private async Task<AttributeOutcome> Execute(Guid assetId, Func<Guid, Task<AttributeOutcome>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty) return AttributeOutcome.Unauthenticated;
        if (assetId == Guid.Empty) return AttributeOutcome.InvalidIdentity;
        var outcome = await action(actor.Value);
        return outcome switch
        {
            AttributeOutcome.Succeeded or AttributeOutcome.Unavailable or AttributeOutcome.Forbidden or AttributeOutcome.Archived or
                AttributeOutcome.InvalidName or AttributeOutcome.InvalidValue or AttributeOutcome.UnsupportedSensitivity or AttributeOutcome.DuplicateName => outcome,
            _ => throw new InvalidOperationException("Unexpected attribute outcome.")
        };
    }
}
