using HomeVault.Domain.Assets;

namespace HomeVault.Application.Assets;

/// <summary>Defines atomic access checking and insertion for a newly created Vault-bound Asset.</summary>
public interface IAssetRegistrationStore
{
    /// <summary>Checks current Vault access and lifecycle together with an insert-only write.</summary>
    /// <param name="asset">A Vault-bound Asset with no attributes or Evidence; do not mutate during the call.</param>
    /// <param name="actorId">A non-empty identity obtained from trusted current-actor context.</param>
    /// <param name="cancellationToken">Cancellation checked before changing storage.</param>
    /// <returns>Added, or an access/lifecycle/conflict outcome without exposing existing metadata.</returns>
    /// <exception cref="ArgumentNullException">The Asset is null.</exception>
    /// <exception cref="ArgumentException">The actor is empty, or the Asset is unbound or prepopulated.</exception>
    /// <exception cref="OperationCanceledException">Cancellation prevents completion.</exception>
    /// <remarks>
    /// Missing Vaults and nonmembers share VaultUnavailable. Viewers receive Forbidden;
    /// authorized writers to archived Vaults receive VaultArchived. Access checks precede
    /// global Asset identity conflict detection. Insert stores an independent snapshot and
    /// never overwrites. Checks and insertion must serialize with Vault state changes.
    /// Unexpected failures propagate; an interrupted response does not prove no commit.
    /// Success promises adapter acceptance, not durability. This contract does not authenticate.
    /// </remarks>
    Task<AssetRegistrationOutcome> RegisterAsync(Asset asset, Guid actorId, CancellationToken cancellationToken);
}
