namespace HomeVault.Application.Assets;

/// <summary>Contains an immutable registered-Asset view or a safe failure.</summary>
public sealed class RegisterAssetResult
{
    internal RegisterAssetResult(RegisteredAsset asset) => Asset = asset;
    internal RegisterAssetResult(RegisterAssetError error) => Error = error;
    /// <summary>Gets whether storage accepted the Asset; this does not imply durability.</summary>
    public bool IsSuccess => Asset is not null;
    /// <summary>Gets the created view, or null on failure.</summary>
    public RegisteredAsset? Asset { get; }
    /// <summary>Gets the safe failure code or None.</summary>
    public RegisterAssetError Error { get; }
    /// <summary>Returns a label without private metadata.</summary>
    /// <returns>The type label.</returns>
    public override string ToString() => nameof(RegisterAssetResult);
}
