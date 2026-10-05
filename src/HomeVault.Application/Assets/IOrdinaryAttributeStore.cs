using HomeVault.Domain.Assets;

namespace HomeVault.Application.Assets;

/// <summary>Atomic current-access boundary for ordinary attributes; never reads or overwrites Sensitive values.</summary>
/// <remarks>Writes check membership, role and lifecycle before Domain validation. Unexpected stored state throws a safe failure.</remarks>
public interface IOrdinaryAttributeStore
{
    /// <summary>Adds a new ordinary entry in an Active Vault.</summary>
    /// <param name="assetId">Target Asset.</param><param name="actorId">Trusted requester.</param>
    /// <param name="name">Name trimmed by Domain.</param><param name="value">Text preserved exactly.</param>
    /// <param name="sensitivity">Explicit Ordinary classification; all other values are rejected.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe mutation outcome.</returns>
    Task<AttributeOutcome> AddAsync(Guid assetId, Guid actorId, string? name, string? value, AttributeSensitivity sensitivity, CancellationToken cancellationToken);
    /// <summary>Changes an existing ordinary value without renaming or reclassification.</summary>
    /// <param name="assetId">Target Asset.</param><param name="actorId">Trusted requester.</param>
    /// <param name="name">Lookup name.</param><param name="value">Replacement text.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe mutation outcome.</returns>
    Task<AttributeOutcome> ChangeAsync(Guid assetId, Guid actorId, string? name, string? value, CancellationToken cancellationToken);
    /// <summary>Removes an existing ordinary entry.</summary>
    /// <param name="assetId">Target Asset.</param><param name="actorId">Trusted requester.</param>
    /// <param name="name">Lookup name.</param><param name="cancellationToken">Operation cancellation.</param>
    /// <returns>A safe mutation outcome.</returns>
    Task<AttributeOutcome> RemoveAsync(Guid assetId, Guid actorId, string? name, CancellationToken cancellationToken);
    /// <summary>Reads a consistent immutable snapshot through current membership, including Archived Vaults.</summary>
    /// <param name="assetId">Target Asset.</param><param name="actorId">Trusted requester.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>Null for absent/inaccessible Assets, otherwise ordinary entries with no ordering guarantee.</returns>
    Task<IReadOnlyList<AssetAttribute>?> ListAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken);
}
