namespace HomeVault.Infrastructure.Persistence;

internal sealed class AssetRow
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public string Name { get; set; } = "";
}
