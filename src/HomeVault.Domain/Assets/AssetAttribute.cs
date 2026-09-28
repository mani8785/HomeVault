using Ardalis.GuardClauses;

namespace HomeVault.Domain.Assets;

/// <summary>An immutable text entry owned by an Asset.</summary>
/// <remarks>Entries are created through Asset operations; values may contain sensitive information.</remarks>
public sealed class AssetAttribute
{
    internal AssetAttribute(string? name, string? value)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name)).Trim();
        Value = Guard.Against.NullOrWhiteSpace(value, nameof(value));
    }

    /// <summary>Gets the trimmed original name spelling. Avoid logging potentially sensitive names.</summary>
    public string Name { get; }

    /// <summary>Gets the nonblank text exactly as supplied. Read deliberately and avoid diagnostic logging.</summary>
    public string Value { get; }

    /// <summary>Returns a safe label without the name or value.</summary>
    /// <returns>The label AssetAttribute.</returns>
    public override string ToString() => nameof(AssetAttribute);
}
