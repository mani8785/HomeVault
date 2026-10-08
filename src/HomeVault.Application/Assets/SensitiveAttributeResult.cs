namespace HomeVault.Application.Assets;

/// <summary>Safe outcomes for deliberately authorized Sensitive operations.</summary>
public enum SensitiveAttributeOutcome
{
    /// <summary>The operation succeeded.</summary>
    Succeeded,
    /// <summary>No trusted actor is available.</summary>
    Unauthenticated,
    /// <summary>An identity is empty.</summary>
    InvalidIdentity,
    /// <summary>The Asset or attribute is absent or inaccessible.</summary>
    Unavailable,
    /// <summary>The current role cannot access Sensitive text or mutate it.</summary>
    Forbidden,
    /// <summary>Mutations are forbidden in an archived Vault.</summary>
    Archived,
    /// <summary>The label is blank.</summary>
    InvalidName,
    /// <summary>The text is blank, invalid Unicode or outside the encryption bound.</summary>
    InvalidValue,
    /// <summary>Sensitive classification must be explicit.</summary>
    UnsupportedSensitivity,
    /// <summary>A matching Ordinary or Sensitive label exists.</summary>
    DuplicateName,
    /// <summary>Protected data or its keys are unavailable; provider details remain private.</summary>
    SensitiveUnavailable
}

/// <summary>Visible metadata only; no value or encryption payload is present.</summary>
/// <param name="Id">Stable server-generated attribute identity.</param>
/// <param name="Name">Trimmed visible label; never put a secret here.</param>
public sealed record SensitiveAttributeMetadata(Guid Id, string Name);

/// <summary>Operation result whose ordinary serialization never discloses plaintext.</summary>
public sealed class SensitiveAttributeResult
{
    private readonly string? _value;
    private SensitiveAttributeResult(SensitiveAttributeOutcome outcome, Guid? id, string? value,
        IReadOnlyList<SensitiveAttributeMetadata>? metadata)
    { Outcome = outcome; Id = id; _value = value; Metadata = metadata; }
    /// <summary>Gets the safe operation outcome.</summary>
    public SensitiveAttributeOutcome Outcome { get; }
    /// <summary>Gets the new identity only for a successful addition.</summary>
    public Guid? Id { get; }
    /// <summary>Gets an immutable metadata snapshot only for a successful listing.</summary>
    public IReadOnlyList<SensitiveAttributeMetadata>? Metadata { get; }
    /// <summary>Creates a safe outcome without private text.</summary>
    /// <param name="outcome">Operation status.</param><returns>A value-free result.</returns>
    public static SensitiveAttributeResult Status(SensitiveAttributeOutcome outcome) => new(outcome, null, null, null);
    /// <summary>Records a successful addition.</summary>
    /// <param name="id">New attribute identity.</param><returns>A value-free result.</returns>
    public static SensitiveAttributeResult Added(Guid id) => new(SensitiveAttributeOutcome.Succeeded, id, null, null);
    /// <summary>Wraps explicitly authorized plaintext without exposing it as a property.</summary>
    /// <param name="value">Private text; never log or format it.</param><returns>A deliberate-read result.</returns>
    public static SensitiveAttributeResult Read(string value) => new(SensitiveAttributeOutcome.Succeeded, null, value, null);
    /// <summary>Copies visible metadata into an immutable snapshot.</summary>
    /// <param name="entries">Metadata without values.</param><returns>A metadata result.</returns>
    public static SensitiveAttributeResult Listed(IEnumerable<SensitiveAttributeMetadata> entries) => new(SensitiveAttributeOutcome.Succeeded, null, null, Array.AsReadOnly(entries.ToArray()));
    /// <summary>Deliberately exposes text already authorized by the storage operation.</summary>
    /// <returns>The exact text; managed strings cannot promise secure erasure.</returns>
    /// <exception cref="InvalidOperationException">This is not a successful value-read result.</exception>
    public string ReadValue() => Outcome == SensitiveAttributeOutcome.Succeeded && _value is not null ? _value : throw new InvalidOperationException("No authorized value is available.");
    /// <summary>Returns a fixed label without metadata or values.</summary><returns>A safe label.</returns>
    public override string ToString() => nameof(SensitiveAttributeResult);
}
