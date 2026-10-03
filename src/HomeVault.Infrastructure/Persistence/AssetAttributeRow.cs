namespace HomeVault.Infrastructure.Persistence;

internal sealed class AssetAttributeRow
{
    public Guid AssetId { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public int Sensitivity { get; set; }
}
