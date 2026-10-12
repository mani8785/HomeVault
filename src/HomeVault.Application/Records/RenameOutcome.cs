namespace HomeVault.Application.Records;

/// <summary>Safe rename outcomes containing no supplied or stored names.</summary>
public enum RenameOutcome
{
    /// <summary>The requested name is stored.</summary>
    Succeeded,
    /// <summary>No valid current actor is available.</summary>
    Unauthenticated,
    /// <summary>The target identity is empty.</summary>
    InvalidIdentity,
    /// <summary>The target is missing or inaccessible.</summary>
    Unavailable,
    /// <summary>The current member cannot rename this record.</summary>
    Forbidden,
    /// <summary>The owning Vault is read-only.</summary>
    Archived,
    /// <summary>The requested name is blank.</summary>
    InvalidName,
    /// <summary>The last-read name is blank.</summary>
    InvalidExpectedName,
    /// <summary>The stored name differs from the last-read name.</summary>
    Conflict
}
