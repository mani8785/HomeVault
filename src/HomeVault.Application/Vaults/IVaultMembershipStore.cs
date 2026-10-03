using HomeVault.Domain.Vaults;

namespace HomeVault.Application.Vaults;

/// <summary>Purpose-specific membership mutations with access checks and commit in one write boundary.</summary>
/// <remarks>Serialize with archival and record writes. Missing/nonmember Vaults share Unavailable.
/// Only Owners manage privileged roles; Administrators manage only Editors/Viewers.
/// Failures must not change stored state. Unexpected failures propagate without retry.</remarks>
public interface IVaultMembershipStore
{
    /// <summary>Adds an existing enabled account after authorizing the requesting member.</summary>
    /// <param name="vaultId">Non-empty target Vault.</param>
    /// <param name="actorId">Non-empty trusted requester, never supplied by the HTTP client.</param>
    /// <param name="targetActorId">Non-empty account to add.</param>
    /// <param name="role">Validated requested role.</param>
    /// <param name="cancellationToken">Cancellation passed through storage.</param>
    /// <returns>Committed success or a safe access, archive, target or duplicate failure.</returns>
    Task<MembershipOutcome> AddAsync(Guid vaultId, Guid actorId, Guid targetActorId, VaultRole role, CancellationToken cancellationToken);

    /// <summary>Changes a membership role, preserving the last Owner even under concurrent writes.</summary>
    /// <param name="vaultId">Non-empty target Vault.</param>
    /// <param name="actorId">Non-empty trusted requester.</param>
    /// <param name="targetActorId">Existing membership to change; its account may be disabled.</param>
    /// <param name="role">Validated replacement role.</param>
    /// <param name="cancellationToken">Cancellation passed through storage.</param>
    /// <returns>Committed success or a safe access, archive, missing-target or last-Owner failure.</returns>
    Task<MembershipOutcome> ChangeRoleAsync(Guid vaultId, Guid actorId, Guid targetActorId, VaultRole role, CancellationToken cancellationToken);

    /// <summary>Removes an existing membership while preserving the final Owner.</summary>
    /// <param name="vaultId">Non-empty target Vault.</param>
    /// <param name="actorId">Non-empty trusted requester.</param>
    /// <param name="targetActorId">Membership to remove; its account may be disabled.</param>
    /// <param name="cancellationToken">Cancellation passed through storage.</param>
    /// <returns>Committed success or a safe access, archive, missing-target or last-Owner failure.</returns>
    Task<MembershipOutcome> RemoveAsync(Guid vaultId, Guid actorId, Guid targetActorId, CancellationToken cancellationToken);
}
