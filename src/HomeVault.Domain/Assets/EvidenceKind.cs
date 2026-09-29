namespace HomeVault.Domain.Assets;

/// <summary>The supported forms of Asset-owned evidence metadata.</summary>
public enum EvidenceKind
{
    /// <summary>An absolute HTTP or HTTPS reference; no resource is fetched.</summary>
    Url,
    /// <summary>Supporting nonblank text.</summary>
    Note
}
