using HomeVault.Application.Identity;
using HomeVault.Domain.Assets;

namespace HomeVault.Application.Assets;

/// <summary>Coordinates Sensitive operations with a once-per-call trusted identity and atomic storage.</summary>
public sealed class SensitiveAttributeUseCases
{
    private readonly ICurrentActor _actor;
    private readonly ISensitiveAttributeStore _store;
    /// <summary>Constructs operations without accessing identity or storage.</summary>
    /// <param name="actor">Trusted current actor.</param><param name="store">Atomic storage boundary.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public SensitiveAttributeUseCases(ICurrentActor actor, ISensitiveAttributeStore store)
    { ArgumentNullException.ThrowIfNull(actor); ArgumentNullException.ThrowIfNull(store); _actor = actor; _store = store; }
    /// <summary>Adds a Sensitive value after atomic authorization.</summary>
    /// <param name="assetId">Target Asset.</param><param name="name">Visible label.</param><param name="value">Private text.</param><param name="sensitivity">Explicit classification.</param><param name="token">Cancellation.</param><returns>A safe result.</returns>
    public Task<SensitiveAttributeResult> AddAsync(Guid assetId, string? name, string? value, AttributeSensitivity sensitivity, CancellationToken token = default) => Execute(assetId, null, actor => _store.AddAsync(assetId, actor, name, value, sensitivity, token), token);
    /// <summary>Replaces private text without changing identity or classification.</summary>
    /// <param name="assetId">Target Asset.</param><param name="attributeId">Attribute identity.</param><param name="value">Private replacement text.</param><param name="token">Cancellation.</param><returns>A safe result.</returns>
    public Task<SensitiveAttributeResult> ChangeAsync(Guid assetId, Guid attributeId, string? value, CancellationToken token = default) => Execute(assetId, attributeId, actor => _store.ChangeAsync(assetId, actor, attributeId, value, token), token);
    /// <summary>Removes one Sensitive attribute.</summary>
    /// <param name="assetId">Target Asset.</param><param name="attributeId">Attribute identity.</param><param name="token">Cancellation.</param><returns>A safe result.</returns>
    public Task<SensitiveAttributeResult> RemoveAsync(Guid assetId, Guid attributeId, CancellationToken token = default) => Execute(assetId, attributeId, actor => _store.RemoveAsync(assetId, actor, attributeId, token), token);
    /// <summary>Lists current-member-visible metadata without private values.</summary>
    /// <param name="assetId">Target Asset.</param><param name="token">Cancellation.</param><returns>A safe metadata result.</returns>
    public Task<SensitiveAttributeResult> ListAsync(Guid assetId, CancellationToken token = default) => Execute(assetId, null, actor => _store.ListAsync(assetId, actor, token), token);
    /// <summary>Deliberately reads a single authorized private value.</summary>
    /// <param name="assetId">Target Asset.</param><param name="attributeId">Attribute identity.</param><param name="token">Cancellation.</param><returns>A deliberate-read result.</returns>
    public Task<SensitiveAttributeResult> ReadAsync(Guid assetId, Guid attributeId, CancellationToken token = default) => Execute(assetId, attributeId, actor => _store.ReadAsync(assetId, actor, attributeId, token), token);
    private Task<SensitiveAttributeResult> Execute(Guid assetId, Guid? attributeId, Func<Guid, Task<SensitiveAttributeResult>> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty) return Task.FromResult(SensitiveAttributeResult.Status(SensitiveAttributeOutcome.Unauthenticated));
        if (assetId == Guid.Empty || attributeId == Guid.Empty) return Task.FromResult(SensitiveAttributeResult.Status(SensitiveAttributeOutcome.InvalidIdentity));
        return operation(actor.Value);
    }
}
