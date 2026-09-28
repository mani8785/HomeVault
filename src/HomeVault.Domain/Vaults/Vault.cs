using Ardalis.GuardClauses;

namespace HomeVault.Domain.Vaults;

/// <summary>An ownership boundary created with one initial Owner and no loaded Assets.</summary>
/// <remarks>Creation does not persist, resolve actor identities, or authorize access.</remarks>
public sealed class Vault
{
    private readonly Dictionary<Guid, VaultMembership> _memberships = new();

    private Vault(Guid id, string? name, VaultType type, Guid initialOwnerId)
    {
        Id = Guard.Against.NullOrEmpty(id, nameof(id));
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Type = Guard.Against.EnumOutOfRange(type, nameof(type));
        var ownerId = Guard.Against.NullOrEmpty(initialOwnerId, nameof(initialOwnerId));
        _memberships.Add(ownerId, new VaultMembership(ownerId, VaultRole.Owner));
    }

    /// <summary>Gets the caller-supplied non-empty Vault identity.</summary>
    public Guid Id { get; }

    /// <summary>Gets the nonblank name exactly as supplied. Names may be private; avoid logging them.</summary>
    public string Name { get; }

    /// <summary>Gets the ownership context.</summary>
    public VaultType Type { get; }

    /// <summary>Gets the lifecycle state, initially Active. Archival operations are not implemented yet.</summary>
    public VaultStatus Status { get; } = VaultStatus.Active;

    /// <summary>Gets an immutable snapshot of current memberships, with no ordering guarantee.</summary>
    /// <remarks>Later mutations do not change earlier snapshots. No Asset collection is loaded.</remarks>
    public IReadOnlyList<VaultMembership> Memberships => Array.AsReadOnly(_memberships.Values.ToArray());

    /// <summary>Adds an actor membership, rejecting duplicates within this Vault.</summary>
    /// <param name="actorId">A non-empty actor identity.</param>
    /// <param name="role">An accepted Vault role.</param>
    /// <returns>None on success, or EmptyActorIdentity, InvalidRole, or DuplicateMember.</returns>
    /// <remarks>Validates identity then role before lookup. Failures preserve state. Callers must authorize this operation in Application.</remarks>
    public VaultMembershipError AddMember(Guid actorId, VaultRole role)
    {
        var error = ValidateMembership(actorId, role);
        if (error != VaultMembershipError.None)
        {
            return error;
        }

        return _memberships.TryAdd(actorId, new VaultMembership(actorId, role))
            ? VaultMembershipError.None
            : VaultMembershipError.DuplicateMember;
    }

    /// <summary>Changes an existing membership's role while preserving at least one Owner.</summary>
    /// <param name="actorId">The non-empty identity of an existing member.</param>
    /// <param name="role">The replacement role.</param>
    /// <returns>None on success, or EmptyActorIdentity, InvalidRole, MemberNotFound, or LastOwner.</returns>
    /// <remarks>Validates identity then role before lookup. An unchanged role succeeds without replacing the entry. Failures preserve state. Application must authorize the role transition.</remarks>
    public VaultMembershipError ChangeMemberRole(Guid actorId, VaultRole role)
    {
        var error = ValidateMembership(actorId, role);
        if (error != VaultMembershipError.None)
        {
            return error;
        }

        if (!_memberships.TryGetValue(actorId, out var member))
        {
            return VaultMembershipError.MemberNotFound;
        }

        if (member.Role == role)
        {
            return VaultMembershipError.None;
        }

        if (IsLastOwner(member))
        {
            return VaultMembershipError.LastOwner;
        }

        _memberships[actorId] = new VaultMembership(actorId, role);
        return VaultMembershipError.None;
    }

    /// <summary>Removes an existing membership unless it is the last Owner.</summary>
    /// <param name="actorId">The non-empty identity of the member to remove.</param>
    /// <returns>None on success, or EmptyActorIdentity, MemberNotFound, or LastOwner.</returns>
    /// <remarks>Failures preserve all memberships. Application must authorize removal; this method does not identify the requesting actor.</remarks>
    public VaultMembershipError RemoveMember(Guid actorId)
    {
        var error = ValidateMembership(actorId, VaultRole.Viewer);
        if (error != VaultMembershipError.None)
        {
            return error;
        }

        if (!_memberships.TryGetValue(actorId, out var member))
        {
            return VaultMembershipError.MemberNotFound;
        }

        if (IsLastOwner(member))
        {
            return VaultMembershipError.LastOwner;
        }

        _memberships.Remove(actorId);
        return VaultMembershipError.None;
    }

    private bool IsLastOwner(VaultMembership member) =>
        member.Role == VaultRole.Owner && _memberships.Values.Count(item => item.Role == VaultRole.Owner) == 1;

    private static VaultMembershipError ValidateMembership(Guid actorId, VaultRole role)
    {
        try
        {
            Guard.Against.NullOrEmpty(actorId, nameof(actorId));
            Guard.Against.EnumOutOfRange(role, nameof(role));
            return VaultMembershipError.None;
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(actorId))
        {
            return VaultMembershipError.EmptyActorIdentity;
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(role))
        {
            return VaultMembershipError.InvalidRole;
        }
    }

    /// <summary>Creates an Active Vault with exactly one initial Owner, or returns a validation failure.</summary>
    /// <param name="id">A non-empty caller-supplied Vault identity.</param>
    /// <param name="name">Nonblank text preserved without trimming or normalization.</param>
    /// <param name="type">Personal, Household, or Organization.</param>
    /// <param name="initialOwnerId">A non-empty actor identity; existence is not checked here.</param>
    /// <returns>A complete Vault on success, otherwise an error with no Vault. Inputs are validated in parameter order.</returns>
    /// <remarks>Does not generate identities, check uniqueness, persist data, or emit events. Errors omit supplied values.</remarks>
    public static VaultCreationResult Create(Guid id, string? name, VaultType type, Guid initialOwnerId)
    {
        try
        {
            return new VaultCreationResult(new Vault(id, name, type, initialOwnerId));
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(id))
        {
            return new VaultCreationResult(VaultCreationError.EmptyIdentity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(name))
        {
            return new VaultCreationResult(VaultCreationError.BlankName);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(type))
        {
            return new VaultCreationResult(VaultCreationError.InvalidType);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(initialOwnerId))
        {
            return new VaultCreationResult(VaultCreationError.EmptyOwnerIdentity);
        }
    }

    /// <summary>Returns a safe label without the name or identifiers.</summary>
    /// <returns>The label Vault.</returns>
    public override string ToString() => nameof(Vault);
}
