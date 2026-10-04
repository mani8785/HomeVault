using HomeVault.Domain.Assets;

namespace HomeVault.Application.Assets;

/// <summary>Atomic current-membership boundary for Asset-owned URL/Note Evidence.</summary>
/// <remarks>Writers require an Active Vault and permitted role. No method fetches resources. Unexpected storage failures propagate safely at the host boundary.</remarks>
public interface IEvidenceStore
{
    /// <summary>Adds Evidence after atomic access checks and Domain validation.</summary>
    /// <param name="assetId">Actual owning Asset.</param><param name="actorId">Trusted requester.</param>
    /// <param name="id">Asset-local Evidence identity.</param><param name="label">Unnormalized label.</param>
    /// <param name="kind">Url or Note.</param><param name="content">Original text, never logged.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe mutation outcome.</returns>
    Task<EvidenceOutcome> AddAsync(Guid assetId, Guid actorId, Guid id, string? label, EvidenceKind kind, string? content, CancellationToken cancellationToken);
    /// <summary>Removes only the stored entry, never an external resource.</summary>
    /// <param name="assetId">Owning Asset.</param><param name="actorId">Trusted requester.</param><param name="id">Evidence identity.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe outcome.</returns>
    Task<EvidenceOutcome> RemoveAsync(Guid assetId, Guid actorId, Guid id, CancellationToken cancellationToken);
    /// <summary>Projects metadata without loading content; Archived Vault members may read.</summary>
    /// <param name="assetId">Owning Asset.</param><param name="actorId">Trusted requester.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>Null for unavailable Asset, otherwise an immutable unordered snapshot.</returns>
    Task<IReadOnlyList<EvidenceMetadata>?> ListAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken);
    /// <summary>Deliberately reads one entry through consistent current membership.</summary>
    /// <param name="assetId">Owning Asset.</param><param name="actorId">Trusted requester.</param><param name="id">Evidence identity.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>Null for unavailable Asset or entry, otherwise an authorized snapshot.</returns>
    Task<EvidenceContent?> ReadContentAsync(Guid assetId, Guid actorId, Guid id, CancellationToken cancellationToken);
}
