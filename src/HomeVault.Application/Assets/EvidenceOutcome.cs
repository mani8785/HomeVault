namespace HomeVault.Application.Assets;

/// <summary>Safe Evidence mutation outcomes without supplied data.</summary>
public enum EvidenceOutcome
{
    /// <summary>The mutation committed.</summary>
    Succeeded,
    /// <summary>No authenticated current actor exists.</summary>
    Unauthenticated,
    /// <summary>An Asset or Evidence identity is empty.</summary>
    InvalidIdentity,
    /// <summary>The Asset is missing/inaccessible, or the target Evidence is absent.</summary>
    Unavailable,
    /// <summary>The current role cannot write.</summary>
    Forbidden,
    /// <summary>The owning Vault is archived.</summary>
    Archived,
    /// <summary>The label is blank.</summary>
    BlankLabel,
    /// <summary>The kind is unsupported.</summary>
    InvalidKind,
    /// <summary>The content is blank.</summary>
    BlankContent,
    /// <summary>The URL does not satisfy Domain validation.</summary>
    InvalidUrl,
    /// <summary>The owning Asset already contains this Evidence identity.</summary>
    DuplicateIdentity
}
