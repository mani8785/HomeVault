namespace HomeVault.Infrastructure.Identity;

internal sealed class AccountCredentialRow
{
    public string Hash { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public long Expires { get; set; }
    public bool Consumed { get; set; }
}
