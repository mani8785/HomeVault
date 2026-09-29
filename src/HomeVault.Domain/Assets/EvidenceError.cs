namespace HomeVault.Domain.Assets;

/// <summary>Safe evidence mutation outcomes without supplied data.</summary>
public enum EvidenceError
{
    /// <summary>The operation succeeded.</summary>
    None,
    /// <summary>The evidence identity is empty.</summary>
    EmptyIdentity,
    /// <summary>The label is null, empty, or entirely whitespace.</summary>
    BlankLabel,
    /// <summary>The kind is unsupported.</summary>
    InvalidKind,
    /// <summary>The content is null, empty, or entirely whitespace.</summary>
    BlankContent,
    /// <summary>The URL does not satisfy the supported reference format.</summary>
    InvalidUrl,
    /// <summary>This Asset already contains the evidence identity.</summary>
    DuplicateIdentity,
    /// <summary>This Asset has no evidence with the supplied identity.</summary>
    NotFound
}
