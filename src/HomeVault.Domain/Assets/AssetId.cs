using Ardalis.GuardClauses;

namespace HomeVault.Domain.Assets;

/// <summary>A validated Asset identity compared by its Guid value.</summary>
/// <remarks>
/// Immutable reference type: there is no default struct value that bypasses validation.
/// This identity does not establish global uniqueness or Vault ownership.
/// </remarks>
public sealed record AssetId
{
    /// <summary>Constructs an identity from a non-empty Guid.</summary>
    /// <param name="id">The identity to preserve.</param>
    /// <exception cref="ArgumentException">The identity is <see cref="Guid.Empty"/>.</exception>
    public AssetId(Guid id)
    {
        Value = Guard.Against.NullOrEmpty(id, nameof(id));
    }

    /// <summary>Gets the validated Guid without generating a replacement identity.</summary>
    public Guid Value { get; }

    /// <summary>Returns the type label without exposing the identity in diagnostics.</summary>
    /// <returns>The label AssetId. Read <see cref="Value"/> explicitly when the identity is needed.</returns>
    public override string ToString() => nameof(AssetId);
}
