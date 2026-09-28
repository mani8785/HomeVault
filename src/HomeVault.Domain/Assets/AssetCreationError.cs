namespace HomeVault.Domain.Assets;

/// <summary>Identifies a validation failure without including caller-supplied values.</summary>
public enum AssetCreationError
{
    /// <summary>Creation succeeded without a validation failure.</summary>
    None,

    /// <summary>The supplied identity was an empty Guid.</summary>
    EmptyIdentity,

    /// <summary>The supplied name was null, empty, or consisted entirely of whitespace.</summary>
    BlankName
}
