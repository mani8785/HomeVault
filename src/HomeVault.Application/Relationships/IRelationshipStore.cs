using HomeVault.Domain.Relationships;

namespace HomeVault.Application.Relationships;

/// <summary>Atomic current-access and actual endpoint ownership boundary for directed Relationships.</summary>
public interface IRelationshipStore
{
    /// <summary>Creates a unique active tuple in an Active Vault after checking both actual Asset owners.</summary>
    /// <param name="vaultId">Intended Vault, not proof of ownership.</param><param name="actorId">Trusted requester.</param>
    /// <param name="id">Globally unique nonempty root identity.</param><param name="sourceAssetId">Source Asset.</param>
    /// <param name="targetAssetId">Target Asset.</param><param name="kind">Directed meaning.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe outcome without references.</returns>
    Task<RelationshipOutcome> CreateAsync(Guid vaultId, Guid actorId, Guid id, Guid sourceAssetId, Guid targetAssetId, RelationshipKind kind, CancellationToken cancellationToken);
    /// <summary>Marks a root Removed, preserving endpoints and checking access even for repeated removal.</summary>
    /// <param name="vaultId">Requested owning Vault.</param><param name="actorId">Trusted requester.</param><param name="id">Root identity.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe outcome.</returns>
    Task<RelationshipOutcome> RemoveAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken cancellationToken);
    /// <summary>Inspects a root through current membership, including Removed roots and Archived Vaults.</summary>
    /// <param name="vaultId">Requested owning Vault.</param><param name="actorId">Trusted requester.</param><param name="id">Root identity.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>Null for unavailable Vault/root; otherwise validated metadata.</returns>
    /// <remarks>Corrupt stored root or ownership fails safely; no endpoint graph is returned.</remarks>
    Task<RelationshipMetadata?> FindAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken cancellationToken);
}
