namespace HomeVault.Domain.Assets;

/// <summary>
/// An independently identified record of something valuable or important.
/// </summary>
/// <remarks>
/// This initial model supports creation and inspection only. Creation does not
/// persist the record, establish Vault ownership, or authorize access.
/// </remarks>
public sealed class Asset
{
    private readonly AssetId _id;
    private readonly AssetName _name;

    private Asset(Guid id, string? name)
    {
        _id = new AssetId(id);
        _name = new AssetName(name);
    }

    /// <summary>Gets the non-empty identity supplied when the Asset was created.</summary>
    public Guid Id => _id.Value;

    /// <summary>Gets the nonblank name exactly as supplied, without trimming or normalization.</summary>
    public string Name => _name.Value;

    /// <summary>Creates an Asset after validating its identity and name with guard clauses.</summary>
    /// <param name="id">The caller-supplied identity; must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="name">A name containing at least one non-whitespace character.</param>
    /// <returns>
    /// A successful result containing the Asset, or a failure containing no Asset.
    /// Identity validation runs first. Failures never include the supplied name.
    /// </returns>
    /// <remarks>
    /// Does not generate an identity, check global uniqueness, or emit events.
    /// Name length and normalization rules are not introduced by this operation.
    /// </remarks>
    public static AssetCreationResult Create(Guid id, string? name)
    {
        try
        {
            return new AssetCreationResult(new Asset(id, name));
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(id))
        {
            return new AssetCreationResult(AssetCreationError.EmptyIdentity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(name))
        {
            return new AssetCreationResult(AssetCreationError.BlankName);
        }
    }
}
