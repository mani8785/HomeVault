namespace HomeVault.Application.Assets;

/// <summary>An authorized content snapshot requiring deliberate access rather than property serialization.</summary>
/// <param name="content">Original validated text; must not be logged.</param>
public sealed class EvidenceContent(string content)
{
    /// <summary>Deliberately returns the already-authorized text without fetching any resource.</summary>
    /// <returns>The original text, including whitespace and URL query/fragment.</returns>
    /// <remarks>Does not reauthorize access or revoke earlier snapshots. Do not log or render as HTML.</remarks>
    public string ReadContent() => content;
    /// <summary>Formats a constant label without content.</summary>
    /// <returns>A safe label.</returns>
    public override string ToString() => nameof(EvidenceContent);
}
