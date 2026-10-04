using Ardalis.GuardClauses;

namespace HomeVault.Domain.Reminders;

/// <summary>An independently identified action due at an exact instant, referencing one Vault and Asset.</summary>
/// <remarks>No notifications are scheduled. Application must verify ownership, access, and Vault lifecycle before writes.</remarks>
public sealed class Reminder
{
    private string _action;

    private Reminder(Guid id, Guid vaultId, Guid assetId, string action, DateTimeOffset dueAt)
    {
        Id = id;
        VaultId = vaultId;
        AssetId = assetId;
        _action = action;
        DueAt = dueAt.ToUniversalTime();
    }

    /// <summary>Gets the non-empty Reminder identity.</summary>
    public Guid Id { get; }

    /// <summary>Gets the intended Vault reference; it does not prove ownership.</summary>
    public Guid VaultId { get; }

    /// <summary>Gets the Asset reference without loading its graph or checking existence.</summary>
    public Guid AssetId { get; }

    /// <summary>Gets the due instant normalized to UTC; past instants and the full representable range are allowed.</summary>
    public DateTimeOffset DueAt { get; private set; }

    /// <summary>Gets the lifecycle state, initially Pending.</summary>
    public ReminderStatus Status { get; private set; } = ReminderStatus.Pending;

    /// <summary>Deliberately reads the original action text.</summary>
    /// <returns>The nonblank action without normalization.</returns>
    /// <remarks>Does not authorize access. Avoid logging the returned text; previously read strings cannot be revoked.</remarks>
    public string ReadAction() => _action;

    /// <summary>Restores validated persisted state without replaying lifecycle operations.</summary>
    /// <param name="id">Root identity.</param><param name="vaultId">Owning Vault identity.</param>
    /// <param name="assetId">Referenced Asset identity.</param><param name="action">Original private action.</param>
    /// <param name="dueAt">Stored instant, normalized to UTC.</param><param name="status">Stored lifecycle.</param>
    /// <returns>A validated independent root.</returns>
    /// <exception cref="InvalidOperationException">Stored state violates Domain invariants.</exception>
    /// <remarks>The adapter must verify actual ownership and requester authorization.</remarks>
    public static Reminder Restore(Guid id, Guid vaultId, Guid assetId, string? action, DateTimeOffset dueAt, ReminderStatus status)
    {
        var result = Create(id, vaultId, assetId, action, dueAt);
        if (result.Reminder is not { } root || !Enum.IsDefined(status))
            throw new InvalidOperationException("Invalid stored Reminder state.");
        root.Status = status;
        return root;
    }

    /// <summary>Creates a Pending action with validated identities and text.</summary>
    /// <param name="id">A non-empty Reminder identity.</param>
    /// <param name="vaultId">A non-empty Vault identity.</param>
    /// <param name="assetId">A non-empty Asset identity.</param>
    /// <param name="action">Nonblank text preserved exactly.</param>
    /// <param name="dueAt">An exact instant normalized to UTC. The caller resolves any local timezone ambiguity.</param>
    /// <returns>A Reminder or safe error, validating id, vaultId, assetId, then action.</returns>
    /// <remarks>Does not check existence, uniqueness, permissions, or archive state. Does not persist or schedule anything.</remarks>
    public static ReminderCreationResult Create(Guid id, Guid vaultId, Guid assetId, string? action, DateTimeOffset dueAt)
    {
        string validAction;
        try
        {
            Guard.Against.NullOrEmpty(id, nameof(id));
            Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
            Guard.Against.NullOrEmpty(assetId, nameof(assetId));
            validAction = Guard.Against.NullOrWhiteSpace(action, nameof(action));
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(id))
        {
            return new ReminderCreationResult(ReminderError.EmptyIdentity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(vaultId))
        {
            return new ReminderCreationResult(ReminderError.EmptyVaultIdentity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(assetId))
        {
            return new ReminderCreationResult(ReminderError.EmptyAssetIdentity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(action))
        {
            return new ReminderCreationResult(ReminderError.BlankAction);
        }

        return new ReminderCreationResult(new Reminder(id, vaultId, assetId, validAction, dueAt));
    }

    /// <summary>Atomically replaces a Pending reminder's action and due instant.</summary>
    /// <param name="action">Nonblank text preserved exactly.</param>
    /// <param name="dueAt">An exact instant normalized to UTC.</param>
    /// <returns>None, NotPending before input validation, or BlankAction. Failures leave all fields unchanged.</returns>
    public ReminderError Update(string? action, DateTimeOffset dueAt)
    {
        if (Status != ReminderStatus.Pending)
        {
            return ReminderError.NotPending;
        }

        string validAction;
        try
        {
            validAction = Guard.Against.NullOrWhiteSpace(action, nameof(action));
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(action))
        {
            return ReminderError.BlankAction;
        }

        var utcDueAt = dueAt.ToUniversalTime();
        _action = validAction;
        DueAt = utcDueAt;
        return ReminderError.None;
    }

    /// <summary>Completes a Pending action; repeated completion succeeds unchanged.</summary>
    /// <returns>None on success or repeated completion, NotPending when Cancelled.</returns>
    public ReminderError Complete() => Finish(ReminderStatus.Completed);

    /// <summary>Cancels a Pending action; repeated cancellation succeeds unchanged.</summary>
    /// <returns>None on success or repeated cancellation, NotPending when Completed.</returns>
    public ReminderError Cancel() => Finish(ReminderStatus.Cancelled);

    /// <summary>Checks whether a Pending action is strictly past its due instant.</summary>
    /// <param name="now">The comparison instant supplied by the caller; no system clock is consulted.</param>
    /// <returns>True only when Pending and DueAt is earlier than now. Equality is not overdue.</returns>
    public bool IsOverdue(DateTimeOffset now) => Status == ReminderStatus.Pending && DueAt < now;

    private ReminderError Finish(ReminderStatus target)
    {
        if (Status != ReminderStatus.Pending && Status != target)
        {
            return ReminderError.NotPending;
        }

        Status = target;
        return ReminderError.None;
    }

    /// <summary>Returns a safe label without action text or identifiers.</summary>
    /// <returns>The label Reminder.</returns>
    public override string ToString() => nameof(Reminder);
}
