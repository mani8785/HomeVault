using HomeVault.Domain.Assets;

namespace HomeVault.Application.Assets;

/// <summary>Immutable Evidence metadata that cannot carry content.</summary>
/// <param name="id">Asset-local Evidence identity.</param>
/// <param name="label">Original label; never put secrets in labels.</param>
/// <param name="kind">Url or Note.</param>
public sealed class EvidenceMetadata(Guid id, string label, EvidenceKind kind)
{
    /// <summary>Gets the Asset-local identity.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the original descriptive label.</summary>
    public string Label { get; } = label;
    /// <summary>Gets Url or Note.</summary>
    public EvidenceKind Kind { get; } = kind;
    /// <summary>Formats a safe label without supplied metadata.</summary>
    /// <returns>A constant label.</returns>
    public override string ToString() => nameof(EvidenceMetadata);
}
