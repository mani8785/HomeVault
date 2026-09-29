namespace HomeVault.Domain.Assets;

/// <summary>Immutable supporting metadata owned by one Asset, without external-resource access.</summary>
public sealed class Evidence
{
    private readonly string _content;

    internal Evidence(Guid id, string label, EvidenceKind kind, string content)
    {
        Id = id;
        Label = label;
        Kind = kind;
        _content = content;
    }

    /// <summary>Gets the non-empty identity used to address the entry within its Asset.</summary>
    public Guid Id { get; }

    /// <summary>Gets the original descriptive label. Labels are visible metadata and must not contain secrets.</summary>
    public string Label { get; }

    /// <summary>Gets whether the content is a URL or a note.</summary>
    public EvidenceKind Kind { get; }

    /// <summary>Deliberately reads the original reference or note text.</summary>
    /// <returns>The content exactly as supplied, without normalization.</returns>
    /// <remarks>Does not authorize access or fetch a resource. Do not log the returned text. Previously read strings and snapshots cannot be revoked.</remarks>
    public string ReadContent() => _content;

    /// <summary>Returns a safe label without identity, label, or content.</summary>
    /// <returns>The label Evidence.</returns>
    public override string ToString() => nameof(Evidence);
}
