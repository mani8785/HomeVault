using Ardalis.GuardClauses;

namespace HomeVault.Domain.Assets;

/// <summary>An immutable text entry owned by an Asset.</summary>
/// <remarks>Entries are created through Asset operations; values may contain sensitive information.</remarks>
public sealed class AssetAttribute
{
    /// <summary>Validates an independent candidate without authorizing access or assigning storage identity.</summary>
    /// <param name="name">Nonblank label, trimmed on success.</param>
    /// <param name="value">Nonblank text preserved exactly; never log it.</param>
    /// <param name="sensitivity">Explicit supported classification.</param>
    /// <returns>A candidate or safe validation error, in name/value/classification order.</returns>
    public static (AssetAttribute? Attribute, AssetAttributeError Error) Create(string? name, string? value, AttributeSensitivity sensitivity)
    {
        try { return (new AssetAttribute(name, value, sensitivity), AssetAttributeError.None); }
        catch (ArgumentException error) when (error.ParamName == nameof(name)) { return (null, AssetAttributeError.BlankName); }
        catch (ArgumentException error) when (error.ParamName == nameof(value)) { return (null, AssetAttributeError.BlankValue); }
        catch (ArgumentException error) when (error.ParamName == nameof(sensitivity)) { return (null, AssetAttributeError.InvalidSensitivity); }
    }

    private readonly string _value;

    internal AssetAttribute(string? name, string? value, AttributeSensitivity sensitivity)
    {
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name)).Trim();
        _value = Guard.Against.NullOrWhiteSpace(value, nameof(value));
        Sensitivity = Guard.Against.EnumOutOfRange(sensitivity, nameof(sensitivity));
    }

    /// <summary>Gets the trimmed descriptive label. Never place secrets in names; names remain visible metadata.</summary>
    public string Name { get; }

    /// <summary>Gets the original Ordinary text, or null for Sensitive text.</summary>
    public string? Value => IsSensitive ? null : _value;

    /// <summary>Gets the caller-selected classification; no content detection or authorization is implied.</summary>
    public AttributeSensitivity Sensitivity { get; }

    /// <summary>Gets whether ordinary property reads conceal the text.</summary>
    public bool IsSensitive => Sensitivity == AttributeSensitivity.Sensitive;

    /// <summary>Deliberately reads the original text for either classification.</summary>
    /// <returns>The nonblank text exactly as supplied.</returns>
    /// <remarks>
    /// This method does not authorize the caller. Application access checks must precede its use.
    /// Do not log the returned text. Previously read strings and snapshots cannot be revoked.
    /// </remarks>
    public string ReadValue() => _value;

    /// <summary>Returns a safe label without the name or value.</summary>
    /// <returns>The label AssetAttribute.</returns>
    public override string ToString() => nameof(AssetAttribute);
}
