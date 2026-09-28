using Ardalis.GuardClauses;

namespace HomeVault.Domain.Assets;

/// <summary>
/// An independently identified record of something valuable or important.
/// </summary>
/// <remarks>
/// Supports creation, inspection, and text attribute mutations. Creation does not
/// persist the record, establish Vault ownership, or authorize access.
/// </remarks>
public sealed class Asset
{
    private readonly AssetId _id;
    private readonly AssetName _name;
    private readonly Dictionary<string, AssetAttribute> _attributes = new(StringComparer.OrdinalIgnoreCase);

    private Asset(Guid id, string? name)
    {
        _id = new AssetId(id);
        _name = new AssetName(name);
    }

    /// <summary>Gets the non-empty identity supplied when the Asset was created.</summary>
    public Guid Id => _id.Value;

    /// <summary>Gets the nonblank name exactly as supplied, without trimming or normalization.</summary>
    public string Name => _name.Value;

    /// <summary>Gets an immutable snapshot of the current attribute entries, with no ordering guarantee.</summary>
    /// <remarks>Later mutations do not change an earlier snapshot. Names and values require deliberate handling.</remarks>
    public IReadOnlyList<AssetAttribute> Attributes => Array.AsReadOnly(_attributes.Values.ToArray());

    /// <summary>Adds a text attribute, rejecting names already present after trimming and ignoring ordinal case.</summary>
    /// <param name="name">A nonblank name; trimmed spelling is retained.</param>
    /// <param name="value">Nonblank text preserved exactly.</param>
    /// <returns>None on success, or BlankName, BlankValue, or DuplicateName. Name validation precedes value validation and lookup.</returns>
    /// <remarks>Failures leave all entries unchanged and never return supplied values.</remarks>
    public AssetAttributeError AddAttribute(string? name, string? value) => SetAttribute(name, value, false);

    /// <summary>Replaces an existing attribute value without changing its original name spelling.</summary>
    /// <param name="name">A nonblank lookup name, trimmed and matched ignoring ordinal case.</param>
    /// <param name="value">Nonblank replacement text preserved exactly.</param>
    /// <returns>None on success, or BlankName, BlankValue, or NotFound. Name validation precedes value validation and lookup.</returns>
    /// <remarks>An identical replacement succeeds. Failures leave all entries unchanged.</remarks>
    public AssetAttributeError ChangeAttribute(string? name, string? value) => SetAttribute(name, value, true);

    /// <summary>Removes one attribute from this Asset instance.</summary>
    /// <param name="name">A nonblank lookup name, trimmed and matched ignoring ordinal case.</param>
    /// <returns>None on success, BlankName for invalid input, or NotFound for a missing entry.</returns>
    /// <remarks>Failure leaves all entries unchanged. No external storage or resource is changed.</remarks>
    public AssetAttributeError RemoveAttribute(string? name)
    {
        try
        {
            var key = Guard.Against.NullOrWhiteSpace(name, nameof(name)).Trim();
            return _attributes.Remove(key) ? AssetAttributeError.None : AssetAttributeError.NotFound;
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(name))
        {
            return AssetAttributeError.BlankName;
        }
    }

    private AssetAttributeError SetAttribute(string? name, string? value, bool replace)
    {
        AssetAttribute candidate;
        try
        {
            candidate = new AssetAttribute(name, value);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(name))
        {
            return AssetAttributeError.BlankName;
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(value))
        {
            return AssetAttributeError.BlankValue;
        }

        var exists = _attributes.TryGetValue(candidate.Name, out var current);
        if (replace)
        {
            if (!exists)
            {
                return AssetAttributeError.NotFound;
            }

            _attributes[candidate.Name] = new AssetAttribute(current!.Name, candidate.Value);
        }
        else if (!exists)
        {
            _attributes.Add(candidate.Name, candidate);
        }
        else
        {
            return AssetAttributeError.DuplicateName;
        }

        return AssetAttributeError.None;
    }

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
