namespace HomeVault.Domain.Assets;

/// <summary>Reports the outcome of creating an Asset without throwing for invalid input.</summary>
/// <remarks>
/// Instances are produced by Asset creation factories. A success contains an
/// Asset and no error; a failure contains an error and no partially valid Asset.
/// This concrete result does not introduce a generic domain result framework.
/// </remarks>
public sealed class AssetCreationResult
{
    internal AssetCreationResult(Asset asset)
    {
        Asset = asset;
        Error = AssetCreationError.None;
    }

    internal AssetCreationResult(AssetCreationError error)
    {
        Error = error;
    }

    /// <summary>Gets whether creation succeeded and an Asset is available.</summary>
    public bool IsSuccess => Asset is not null;

    /// <summary>Gets the created Asset on success, or null on validation failure.</summary>
    public Asset? Asset { get; }

    /// <summary>Gets the validation failure, or <see cref="AssetCreationError.None"/> on success.</summary>
    public AssetCreationError Error { get; }
}
