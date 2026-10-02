namespace HomeVault.Infrastructure.Identity;

/// <summary>A one-use credential for protected export only. Never log or serialize into API responses.</summary>
public sealed class AccountCredential
{
    /// <summary>Export format version.</summary>
    public int Version => 1;
    /// <summary>Invitation or recovery purpose.</summary>
    public required string Purpose { get; init; }
    /// <summary>Recipient identifier; personal information, not a log field.</summary>
    public required string Login { get; init; }
    /// <summary>Secret bearer credential; export only through the private operator channel.</summary>
    public required string Secret { get; init; }
    /// <summary>UTC expiry for the recipient.</summary>
    public required DateTimeOffset Expires { get; init; }
    /// <summary>Returns a constant without credential contents.</summary>
    public override string ToString() => nameof(AccountCredential);
}
