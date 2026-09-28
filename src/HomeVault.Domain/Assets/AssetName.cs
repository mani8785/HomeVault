using Ardalis.GuardClauses;

namespace HomeVault.Domain.Assets;

/// <summary>A nonblank Asset name with exact, ordinal value equality.</summary>
/// <remarks>
/// Preserves case, whitespace, and Unicode representation. Does not impose length
/// limits or uniqueness. The immutable value cannot be replaced through an initializer.
/// </remarks>
public sealed record AssetName
{
    /// <summary>Constructs a name containing at least one non-whitespace character.</summary>
    /// <param name="name">The text to preserve without trimming or normalization.</param>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    /// <exception cref="ArgumentException">The name is empty or entirely whitespace.</exception>
    public AssetName(string? name)
    {
        Value = Guard.Against.NullOrWhiteSpace(name, nameof(name));
    }

    /// <summary>Gets the original validated text; callers must avoid exposing sensitive names.</summary>
    public string Value { get; }

    /// <summary>Returns the type label without exposing the name in diagnostics.</summary>
    /// <returns>The label AssetName. Read <see cref="Value"/> explicitly when the text is needed.</returns>
    public override string ToString() => nameof(AssetName);
}
