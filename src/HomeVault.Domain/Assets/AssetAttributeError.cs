namespace HomeVault.Domain.Assets;

/// <summary>Safe outcomes for Asset attribute mutations, without supplied names or values.</summary>
public enum AssetAttributeError
{
    /// <summary>The operation succeeded.</summary>
    None,
    /// <summary>The name was null, empty, or entirely whitespace.</summary>
    BlankName,
    /// <summary>The value was null, empty, or entirely whitespace.</summary>
    BlankValue,
    /// <summary>An attribute already matches the trimmed name, ignoring ordinal case.</summary>
    DuplicateName,
    /// <summary>No attribute matches the trimmed name, ignoring ordinal case.</summary>
    NotFound,
    /// <summary>The supplied classification is not a supported sensitivity value.</summary>
    InvalidSensitivity
}
