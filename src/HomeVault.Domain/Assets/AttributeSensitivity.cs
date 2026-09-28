namespace HomeVault.Domain.Assets;

/// <summary>Caller-selected disclosure classification for an attribute's text.</summary>
public enum AttributeSensitivity
{
    /// <summary>Text may be exposed by ordinary property inspection and serialization.</summary>
    Ordinary,
    /// <summary>Text requires deliberate method access and is hidden from ordinary property inspection.</summary>
    Sensitive
}
