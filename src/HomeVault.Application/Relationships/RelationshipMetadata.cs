using HomeVault.Domain.Relationships;

namespace HomeVault.Application.Relationships;

/// <summary>Immutable authorized Relationship inspection without loaded Asset data.</summary>
/// <param name="id">Root identity.</param><param name="vaultId">Owning Vault.</param>
/// <param name="sourceAssetId">Source reference.</param><param name="targetAssetId">Target reference.</param>
/// <param name="kind">Directed meaning.</param><param name="status">Active or Removed.</param>
public sealed class RelationshipMetadata(Guid id, Guid vaultId, Guid sourceAssetId, Guid targetAssetId, RelationshipKind kind, RelationshipStatus status)
{
    /// <summary>Gets the root identity.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the owning Vault identity.</summary>
    public Guid VaultId { get; } = vaultId;
    /// <summary>Gets the source Asset identity.</summary>
    public Guid SourceAssetId { get; } = sourceAssetId;
    /// <summary>Gets the target Asset identity.</summary>
    public Guid TargetAssetId { get; } = targetAssetId;
    /// <summary>Gets the supported directed meaning.</summary>
    public RelationshipKind Kind { get; } = kind;
    /// <summary>Gets the retained lifecycle state.</summary>
    public RelationshipStatus Status { get; } = status;
    /// <summary>Formats a safe label without references.</summary>
    /// <returns>A constant label.</returns>
    public override string ToString() => nameof(RelationshipMetadata);
}
