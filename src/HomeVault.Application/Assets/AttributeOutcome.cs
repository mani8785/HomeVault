namespace HomeVault.Application.Assets;

/// <summary>Safe outcomes of ordinary attribute operations; never contains supplied data.</summary>
public enum AttributeOutcome
{
    /// <summary>The operation completed.</summary>
    Succeeded,
    /// <summary>No authenticated current actor exists.</summary>
    Unauthenticated,
    /// <summary>The target identity is empty.</summary>
    InvalidIdentity,
    /// <summary>The Asset is absent/inaccessible, or the requested attribute is absent.</summary>
    Unavailable,
    /// <summary>The current role cannot write.</summary>
    Forbidden,
    /// <summary>The owning Vault is archived.</summary>
    Archived,
    /// <summary>The attribute name is blank.</summary>
    InvalidName,
    /// <summary>The attribute value is blank.</summary>
    InvalidValue,
    /// <summary>Only explicit Ordinary classification is supported.</summary>
    UnsupportedSensitivity,
    /// <summary>An ordinal-ignore-case matching name already exists.</summary>
    DuplicateName
}
