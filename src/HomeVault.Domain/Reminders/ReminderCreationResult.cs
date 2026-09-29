namespace HomeVault.Domain.Reminders;

/// <summary>Contains a locally valid Reminder or a safe validation failure.</summary>
public sealed class ReminderCreationResult
{
    internal ReminderCreationResult(Reminder reminder) => Reminder = reminder;

    internal ReminderCreationResult(ReminderError error) => Error = error;

    /// <summary>Gets whether creation succeeded.</summary>
    public bool IsSuccess => Reminder is not null;

    /// <summary>Gets the created Reminder, or null on failure.</summary>
    public Reminder? Reminder { get; }

    /// <summary>Gets the validation error, or None on success.</summary>
    public ReminderError Error { get; }

    /// <summary>Returns a label without exposing action text or identifiers.</summary>
    /// <returns>The label ReminderCreationResult.</returns>
    public override string ToString() => nameof(ReminderCreationResult);
}
