namespace HomeVault.Domain.Reminders;

/// <summary>Safe Reminder operation outcomes without caller-supplied data.</summary>
public enum ReminderError
{
    /// <summary>The operation succeeded.</summary>
    None,
    /// <summary>The Reminder identity is empty.</summary>
    EmptyIdentity,
    /// <summary>The Vault identity is empty.</summary>
    EmptyVaultIdentity,
    /// <summary>The Asset identity is empty.</summary>
    EmptyAssetIdentity,
    /// <summary>The action is null, empty, or entirely whitespace.</summary>
    BlankAction,
    /// <summary>The operation requires Pending state and is not an idempotent terminal operation.</summary>
    NotPending
}
