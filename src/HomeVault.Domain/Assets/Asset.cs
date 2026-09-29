using Ardalis.GuardClauses;

namespace HomeVault.Domain.Assets;

/// <summary>
/// An independently identified record of something valuable or important.
/// </summary>
/// <remarks>
/// Supports creation, inspection, text attributes, and evidence metadata. Creation does not
/// persist the record, establish Vault ownership, or authorize access.
/// </remarks>
public sealed class Asset
{
    private readonly AssetId _id;
    private readonly AssetName _name;
    private readonly Dictionary<string, AssetAttribute> _attributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, Evidence> _evidence = new();

    private Asset(Guid id, string? name)
    {
        _id = new AssetId(id);
        _name = new AssetName(name);
    }

    /// <summary>Gets the non-empty identity supplied when the Asset was created.</summary>
    public Guid Id => _id.Value;

    /// <summary>Gets the nonblank name exactly as supplied, without trimming or normalization.</summary>
    public string Name => _name.Value;

    /// <summary>Gets an immutable snapshot of evidence metadata, with no ordering guarantee.</summary>
    /// <remarks>Later removal does not revoke earlier snapshots. Content requires deliberate method access.</remarks>
    public IReadOnlyList<Evidence> Evidence => Array.AsReadOnly(_evidence.Values.ToArray());

    /// <summary>Adds validated supporting metadata without fetching a URL or storing file bytes.</summary>
    /// <param name="id">A non-empty identity unique within this Asset's evidence.</param>
    /// <param name="label">A nonblank descriptive label preserved exactly; never place secrets in labels.</param>
    /// <param name="kind">Url or Note.</param>
    /// <param name="content">Nonblank text preserved exactly. URLs must be absolute HTTP/HTTPS with a host, no user-info, and no raw whitespace or control characters.</param>
    /// <returns>None or a safe validation/duplicate error. Checks id, label, kind, content, URL format, then duplicate identity.</returns>
    /// <remarks>Failures preserve all entries. Same label/content with a different identity is allowed. Application must enforce Vault access and lifecycle; this operation does not verify reachability or safety to fetch.</remarks>
    public EvidenceError AddEvidence(Guid id, string? label, EvidenceKind kind, string? content)
    {
        string validLabel;
        string validContent;
        try
        {
            Guard.Against.NullOrEmpty(id, nameof(id));
            validLabel = Guard.Against.NullOrWhiteSpace(label, nameof(label));
            Guard.Against.EnumOutOfRange(kind, nameof(kind));
            validContent = Guard.Against.NullOrWhiteSpace(content, nameof(content));
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(id))
        {
            return EvidenceError.EmptyIdentity;
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(label))
        {
            return EvidenceError.BlankLabel;
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(kind))
        {
            return EvidenceError.InvalidKind;
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(content))
        {
            return EvidenceError.BlankContent;
        }

        if (kind == EvidenceKind.Url &&
            (validContent.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) ||
             !Uri.TryCreate(validContent, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
             string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)))
        {
            return EvidenceError.InvalidUrl;
        }

        return _evidence.TryAdd(id, new Evidence(id, validLabel, kind, validContent))
            ? EvidenceError.None
            : EvidenceError.DuplicateIdentity;
    }

    /// <summary>Removes one evidence entry from this Asset only.</summary>
    /// <param name="id">The non-empty evidence identity.</param>
    /// <returns>None on success, EmptyIdentity for invalid input, or NotFound.</returns>
    /// <remarks>Does not delete external resources or revoke earlier snapshots. Failures preserve state. Application must authorize the operation and check Vault lifecycle.</remarks>
    public EvidenceError RemoveEvidence(Guid id)
    {
        try
        {
            Guard.Against.NullOrEmpty(id, nameof(id));
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(id))
        {
            return EvidenceError.EmptyIdentity;
        }

        return _evidence.Remove(id) ? EvidenceError.None : EvidenceError.NotFound;
    }

    /// <summary>Gets an immutable snapshot of the current attribute entries, with no ordering guarantee.</summary>
    /// <remarks>Later mutations do not change an earlier snapshot. Names and values require deliberate handling.</remarks>
    public IReadOnlyList<AssetAttribute> Attributes => Array.AsReadOnly(_attributes.Values.ToArray());

    /// <summary>Adds a text attribute, rejecting names already present after trimming and ignoring ordinal case.</summary>
    /// <param name="name">A nonblank name; trimmed spelling is retained.</param>
    /// <param name="value">Nonblank text preserved exactly.</param>
    /// <param name="sensitivity">An explicit classification; use Sensitive for private values. No automatic content detection occurs.</param>
    /// <returns>None on success, or BlankName, BlankValue, InvalidSensitivity, or DuplicateName. Validation checks name, value, then classification before lookup.</returns>
    /// <remarks>Failures leave all entries unchanged and never return supplied values.</remarks>
    public AssetAttributeError AddAttribute(string? name, string? value, AttributeSensitivity sensitivity) => SetAttribute(name, value, false, sensitivity);

    /// <summary>Replaces an existing attribute value without changing its original name spelling.</summary>
    /// <param name="name">A nonblank lookup name, trimmed and matched ignoring ordinal case.</param>
    /// <param name="value">Nonblank replacement text preserved exactly.</param>
    /// <returns>None on success, or BlankName, BlankValue, or NotFound. Name validation precedes value validation and lookup.</returns>
    /// <remarks>An identical replacement succeeds. Classification is preserved; failures leave all entries unchanged.</remarks>
    public AssetAttributeError ChangeAttribute(string? name, string? value) => SetAttribute(name, value, true, AttributeSensitivity.Sensitive);

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

    private AssetAttributeError SetAttribute(string? name, string? value, bool replace, AttributeSensitivity sensitivity)
    {
        AssetAttribute candidate;
        try
        {
            candidate = new AssetAttribute(name, value, sensitivity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(name))
        {
            return AssetAttributeError.BlankName;
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(value))
        {
            return AssetAttributeError.BlankValue;
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(sensitivity))
        {
            return AssetAttributeError.InvalidSensitivity;
        }

        var exists = _attributes.TryGetValue(candidate.Name, out var current);
        if (replace)
        {
            if (!exists)
            {
                return AssetAttributeError.NotFound;
            }

            _attributes[candidate.Name] = new AssetAttribute(current!.Name, candidate.ReadValue(), current.Sensitivity);
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
