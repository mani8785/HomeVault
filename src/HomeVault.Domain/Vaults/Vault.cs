using Ardalis.GuardClauses;

namespace HomeVault.Domain.Vaults;

/// <summary>An ownership boundary created with one initial Owner and no loaded Assets.</summary>
/// <remarks>Creation does not persist, resolve actor identities, or authorize access.</remarks>
public sealed class Vault
{
    private readonly Dictionary<Guid, VaultMembership> _memberships = new();

    private Vault(Guid id, string? name, VaultType type)
    {
        Id = Guard.Against.NullOrEmpty(id, nameof(id));
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Type = Guard.Against.EnumOutOfRange(type, nameof(type));
    }

    private Vault(Guid id, string? name, VaultType type, Guid initialOwnerId) : this(id, name, type)
    {
        var ownerId = Guard.Against.NullOrEmpty(initialOwnerId, nameof(initialOwnerId));
        _memberships.Add(ownerId, new VaultMembership(ownerId, VaultRole.Owner));
    }

    /// <summary>Gets the caller-supplied non-empty Vault identity.</summary>
    public Guid Id { get; }

    /// <summary>Gets the nonblank name exactly as supplied. Names may be private; avoid logging them.</summary>
    public string Name { get; }

    /// <summary>Gets the ownership context.</summary>
    public VaultType Type { get; }

    /// <summary>Gets the lifecycle state, initially Active; archival is irreversible through this API.</summary>
    public VaultStatus Status { get; private set; } = VaultStatus.Active;

    /// <summary>Restores a complete validated snapshot without creating membership or loading Assets.</summary>
    /// <param name="id">Stored non-empty identity.</param>
    /// <param name="name">Stored nonblank name, preserved exactly.</param>
    /// <param name="type">Stored ownership context.</param>
    /// <param name="status">Stored lifecycle state.</param>
    /// <param name="memberships">Complete actor/role pairs, copied into private state.</param>
    /// <returns>A restored Vault with at least one Owner.</returns>
    /// <exception cref="InvalidOperationException">The snapshot violates a Vault invariant; the message omits stored values.</exception>
    /// <remarks>This is not an authorization boundary. No creation behavior or events are replayed.</remarks>
    public static Vault Restore(Guid id, string? name, VaultType type, VaultStatus status,
        IEnumerable<KeyValuePair<Guid, VaultRole>> memberships)
    {
        try
        {
            var vault = new Vault(id, name, type);
            vault.Status = Guard.Against.EnumOutOfRange(status, nameof(status));
            Guard.Against.Null(memberships, nameof(memberships));
            foreach (var member in memberships)
            {
                Guard.Against.NullOrEmpty(member.Key, nameof(memberships));
                Guard.Against.EnumOutOfRange(member.Value, nameof(memberships));
                if (!vault._memberships.TryAdd(member.Key, new VaultMembership(member.Key, member.Value)))
                    throw new InvalidOperationException("Invalid stored Vault state.");
            }
            if (!vault._memberships.Values.Any(member => member.Role == VaultRole.Owner))
                throw new InvalidOperationException("Invalid stored Vault state.");
            return vault;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Invalid stored Vault state.");
        }
    }

    /// <summary>Archives this Vault when the supplied actor is a current Owner.</summary>
    /// <param name="requestingActorId">The authenticated actor identity supplied by the application boundary.</param>
    /// <returns>None on success, EmptyActorIdentity for an empty identity, or NotOwner for any other non-owner.</returns>
    /// <remarks>
    /// Checks ownership even when already archived. Repeated Owner requests succeed without changes.
    /// Retains all metadata and memberships for permitted reads. This method does not authenticate
    /// the supplied identity; Application must bind it to the caller and enforce access to the Vault.
    /// </remarks>
    public VaultArchiveError Archive(Guid requestingActorId)
    {
        try
        {
            Guard.Against.NullOrEmpty(requestingActorId, nameof(requestingActorId));
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(requestingActorId))
        {
            return VaultArchiveError.EmptyActorIdentity;
        }

        if (!_memberships.TryGetValue(requestingActorId, out var requester) || requester.Role != VaultRole.Owner)
        {
            return VaultArchiveError.NotOwner;
        }

        Status = VaultStatus.Archived;
        return VaultArchiveError.None;
    }

    /// <summary>Gets an immutable snapshot of current memberships, with no ordering guarantee.</summary>
    /// <remarks>Later mutations do not change earlier snapshots. No Asset collection is loaded.</remarks>
    public IReadOnlyList<VaultMembership> Memberships => Array.AsReadOnly(_memberships.Values.ToArray());

    /// <summary>Adds an actor membership, rejecting duplicates within this Vault.</summary>
    /// <param name="actorId">A non-empty actor identity.</param>
    /// <param name="role">An accepted Vault role.</param>
    /// <returns>None on success, or Archived, EmptyActorIdentity, InvalidRole, or DuplicateMember. Archived takes precedence.</returns>
    /// <remarks>Validates identity then role before lookup. Failures preserve state. Callers must authorize this operation in Application.</remarks>
    public VaultMembershipError AddMember(Guid actorId, VaultRole role)
    {
        if (Status == VaultStatus.Archived)
        {
            return VaultMembershipError.Archived;
        }

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
    /// <returns>None on success, or Archived, EmptyActorIdentity, InvalidRole, MemberNotFound, or LastOwner. Archived takes precedence, including unchanged roles.</returns>
    /// <remarks>Validates identity then role before lookup. An unchanged role succeeds without replacing the entry. Failures preserve state. Application must authorize the role transition.</remarks>
    public VaultMembershipError ChangeMemberRole(Guid actorId, VaultRole role)
    {
        if (Status == VaultStatus.Archived)
        {
            return VaultMembershipError.Archived;
        }

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
    /// <returns>None on success, or Archived, EmptyActorIdentity, MemberNotFound, or LastOwner. Archived takes precedence.</returns>
    /// <remarks>Failures preserve all memberships. Application must authorize removal; this method does not identify the requesting actor.</remarks>
    public VaultMembershipError RemoveMember(Guid actorId)
    {
        if (Status == VaultStatus.Archived)
        {
            return VaultMembershipError.Archived;
        }

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
