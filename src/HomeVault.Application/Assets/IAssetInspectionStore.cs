namespace HomeVault.Application.Assets;

/// <summary>Reads one Asset and current membership from a consistent storage view.</summary>
public interface IAssetInspectionStore
{
    /// <summary>Returns immutable metadata only for a current member of the owning Vault.</summary>
    /// <param name="assetId">The non-empty target Asset identity.</param>
    /// <param name="actorId">The non-empty trusted current actor identity.</param>
    /// <param name="cancellationToken">Cancellation for storage access.</param>
    /// <returns>The view, or null for both missing and inaccessible records. Members may read archived Vaults.</returns>
    /// <remarks>Never expose an unscoped fallback result. Unexpected storage failures propagate without logging private values.</remarks>
    Task<InspectedAsset?> FindAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken);
}
