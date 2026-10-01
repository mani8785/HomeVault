namespace HomeVault.Infrastructure.Persistence;

internal sealed class VaultRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public int Status { get; set; }
}
