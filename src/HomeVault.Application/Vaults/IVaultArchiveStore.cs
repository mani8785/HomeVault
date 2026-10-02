namespace HomeVault.Application.Vaults;

/// <summary>Atomic current-membership authorization and durable Vault archival.</summary>
public interface IVaultArchiveStore
{
    /// <summary>Checks access and archives within one write boundary, serializing with record and membership writes.</summary>
    /// <param name="vaultId">Non-empty target identity.</param>
    /// <param name="actorId">Non-empty trusted requesting actor.</param>
    /// <param name="cancellationToken">Cancellation propagated without automatic retry.</param>
    /// <returns>Archived after commit, Unavailable for missing/nonmember, or Forbidden for other members.</returns>
    /// <remarks>Check ownership even for repeated archival. Preserve all data. Unexpected failures propagate without exposing stored values.</remarks>
    Task<ArchiveVaultOutcome> ArchiveAsync(Guid vaultId, Guid actorId, CancellationToken cancellationToken);
}
