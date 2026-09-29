namespace HomeVault.Domain.Reminders;

/// <summary>The lifecycle of a recorded action, independent of notification delivery.</summary>
public enum ReminderStatus
{
    /// <summary>The action remains outstanding and may be updated.</summary>
    Pending,
    /// <summary>The action was completed; further updates are prohibited.</summary>
    Completed,
    /// <summary>The action was cancelled; further updates are prohibited.</summary>
    Cancelled
}
