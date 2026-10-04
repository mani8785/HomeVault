namespace HomeVault.Application.Reminders;

/// <summary>Safe operation outcomes without private action text or foreign identities.</summary>
public enum ReminderOutcome
{
    /// <summary>The operation committed or an authorized terminal repeat succeeded.</summary>
    Succeeded,
    /// <summary>No trusted actor is present.</summary>
    Unauthenticated,
    /// <summary>A required identity is empty.</summary>
    InvalidIdentity,
    /// <summary>The requested resource is missing or inaccessible.</summary>
    Unavailable,
    /// <summary>The member cannot write.</summary>
    Forbidden,
    /// <summary>The Vault is archived.</summary>
    Archived,
    /// <summary>Action text is blank.</summary>
    BlankAction,
    /// <summary>The same-Vault identity is already reserved.</summary>
    IdentityConflict,
    /// <summary>The requested transition requires Pending state.</summary>
    NotPending
}
