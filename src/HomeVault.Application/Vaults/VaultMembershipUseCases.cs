using HomeVault.Application.Identity;
using HomeVault.Domain.Vaults;

namespace HomeVault.Application.Vaults;

/// <summary>Coordinates membership operations using trusted current identity and atomic storage.</summary>
public sealed class VaultMembershipUseCases
{
    private readonly ICurrentActor _actor;
    private readonly IVaultMembershipStore _store;

    /// <summary>Constructs the operations without accessing identity or storage.</summary>
    /// <param name="actor">Trusted identity source, read once per invocation.</param>
    /// <param name="store">Atomic access and mutation boundary.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public VaultMembershipUseCases(ICurrentActor actor, IVaultMembershipStore store)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(store);
        _actor = actor;
        _store = store;
    }

    /// <summary>Adds an existing enabled account as a member of an Active Vault.</summary>
    /// <param name="vaultId">Target Vault.</param>
    /// <param name="targetActorId">Account to add, not the requester.</param>
    /// <param name="role">Proposed membership role.</param>
    /// <param name="cancellationToken">Cancellation checked before identity access and passed to storage.</param>
    /// <returns>A safe result after awaiting the storage outcome.</returns>
    /// <remarks>Identity validation precedes arguments. Unexpected storage failures propagate without retry or logging.</remarks>
    public Task<MembershipOutcome> AddAsync(Guid vaultId, Guid targetActorId, VaultRole role, CancellationToken cancellationToken = default) =>
        Execute(vaultId, targetActorId, role, actor => _store.AddAsync(vaultId, actor, targetActorId, role, cancellationToken), cancellationToken);

    /// <summary>Changes a role with current access and last-Owner protection.</summary>
    /// <param name="vaultId">Target Vault.</param>
    /// <param name="targetActorId">Membership to change.</param>
    /// <param name="role">Replacement role.</param>
    /// <param name="cancellationToken">Cancellation checked before identity access and passed to storage.</param>
    /// <returns>A safe result after awaiting the storage outcome.</returns>
    public Task<MembershipOutcome> ChangeRoleAsync(Guid vaultId, Guid targetActorId, VaultRole role, CancellationToken cancellationToken = default) =>
        Execute(vaultId, targetActorId, role, actor => _store.ChangeRoleAsync(vaultId, actor, targetActorId, role, cancellationToken), cancellationToken);

    /// <summary>Removes membership without deleting the account or Vault records.</summary>
    /// <param name="vaultId">Target Vault.</param>
    /// <param name="targetActorId">Membership to remove.</param>
    /// <param name="cancellationToken">Cancellation checked before identity access and passed to storage.</param>
    /// <returns>A safe result after awaiting the storage outcome.</returns>
    public Task<MembershipOutcome> RemoveAsync(Guid vaultId, Guid targetActorId, CancellationToken cancellationToken = default) =>
        Execute(vaultId, targetActorId, null, actor => _store.RemoveAsync(vaultId, actor, targetActorId, cancellationToken), cancellationToken);

    private async Task<MembershipOutcome> Execute(Guid vaultId, Guid targetActorId, VaultRole? role,
        Func<Guid, Task<MembershipOutcome>> mutation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var actor = _actor.ActorId;
        if (actor is null || actor == Guid.Empty) return MembershipOutcome.Unauthenticated;
        if (vaultId == Guid.Empty || targetActorId == Guid.Empty) return MembershipOutcome.InvalidIdentity;
        if (role.HasValue && !Enum.IsDefined(role.Value)) return MembershipOutcome.InvalidRole;
        var result = await mutation(actor.Value);
        return result switch
        {
            MembershipOutcome.Succeeded or MembershipOutcome.Unavailable or MembershipOutcome.Forbidden or
                MembershipOutcome.Archived or MembershipOutcome.DuplicateMember or MembershipOutcome.LastOwner => result,
            _ => throw new InvalidOperationException("Unexpected membership outcome.")
        };
    }
}
