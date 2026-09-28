using Ardalis.GuardClauses;

namespace HomeVault.Domain.Relationships;

/// <summary>An independently identified, directed association using stable Asset and Vault references.</summary>
/// <remarks>Contains no loaded graph. Application must verify endpoint existence, ownership, access, active Vault state, and duplicates.</remarks>
public sealed class Relationship
{
    private Relationship(Guid id, Guid vaultId, Guid sourceAssetId, Guid targetAssetId, RelationshipKind kind)
    {
        Id = id;
        VaultId = vaultId;
        SourceAssetId = sourceAssetId;
        TargetAssetId = targetAssetId;
        Kind = kind;
    }

    /// <summary>Gets the non-empty Relationship identity.</summary>
    public Guid Id { get; }

    /// <summary>Gets the non-empty Vault reference; this value alone does not prove endpoint ownership.</summary>
    public Guid VaultId { get; }

    /// <summary>Gets the non-empty source Asset reference.</summary>
    public Guid SourceAssetId { get; }

    /// <summary>Gets the distinct, non-empty target Asset reference.</summary>
    public Guid TargetAssetId { get; }

    /// <summary>Gets the directed meaning, from source to target.</summary>
    public RelationshipKind Kind { get; }

    /// <summary>Gets the lifecycle state, initially Active.</summary>
    public RelationshipStatus Status { get; private set; } = RelationshipStatus.Active;

    /// <summary>Creates a locally valid association without loading its endpoints.</summary>
    /// <param name="id">A non-empty caller-supplied Relationship identity.</param>
    /// <param name="vaultId">The non-empty intended Vault identity.</param>
    /// <param name="sourceAssetId">The non-empty source Asset identity.</param>
    /// <param name="targetAssetId">The non-empty target identity, distinct from the source.</param>
    /// <param name="kind">The supported association kind.</param>
    /// <returns>A complete Relationship or safe error. Validates identifiers in parameter order, then kind, then self-reference.</returns>
    /// <remarks>Does not verify existence, categories, same-Vault ownership, uniqueness, permissions, or archive state. Does not persist or create reverse links.</remarks>
    public static RelationshipCreationResult Create(Guid id, Guid vaultId, Guid sourceAssetId, Guid targetAssetId, RelationshipKind kind)
    {
        try
        {
            Guard.Against.NullOrEmpty(id, nameof(id));
            Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
            Guard.Against.NullOrEmpty(sourceAssetId, nameof(sourceAssetId));
            Guard.Against.NullOrEmpty(targetAssetId, nameof(targetAssetId));
            Guard.Against.EnumOutOfRange(kind, nameof(kind));
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(id))
        {
            return new RelationshipCreationResult(RelationshipCreationError.EmptyIdentity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(vaultId))
        {
            return new RelationshipCreationResult(RelationshipCreationError.EmptyVaultIdentity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(sourceAssetId))
        {
            return new RelationshipCreationResult(RelationshipCreationError.EmptySourceIdentity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(targetAssetId))
        {
            return new RelationshipCreationResult(RelationshipCreationError.EmptyTargetIdentity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(kind))
        {
            return new RelationshipCreationResult(RelationshipCreationError.InvalidKind);
        }

        if (sourceAssetId == targetAssetId)
        {
            return new RelationshipCreationResult(RelationshipCreationError.SelfReference);
        }

        return new RelationshipCreationResult(new Relationship(id, vaultId, sourceAssetId, targetAssetId, kind));
    }

    /// <summary>Marks this association Removed, retaining its references and kind.</summary>
    /// <remarks>Repeated removal succeeds unchanged. Does not delete either endpoint, persist changes, or authorize the caller. Application must check access and Vault lifecycle before invocation.</remarks>
    public void Remove() => Status = RelationshipStatus.Removed;

    /// <summary>Returns a safe label without exposing references.</summary>
    /// <returns>The label Relationship.</returns>
    public override string ToString() => nameof(Relationship);
}
