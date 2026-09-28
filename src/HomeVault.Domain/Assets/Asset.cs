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
    private Asset(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Gets the non-empty identity supplied when the Asset was created.</summary>
    public Guid Id { get; }

    /// <summary>Gets the nonblank name exactly as supplied, without trimming or normalization.</summary>
    public string Name { get; }

    /// <summary>Creates an Asset after manually validating its identity and name.</summary>
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
        if (id == Guid.Empty)
        {
            return new AssetCreationResult(AssetCreationError.EmptyIdentity);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return new AssetCreationResult(AssetCreationError.BlankName);
        }

        return new AssetCreationResult(new Asset(id, name));
    }
}
