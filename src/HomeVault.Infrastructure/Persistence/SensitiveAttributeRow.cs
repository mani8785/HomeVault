namespace HomeVault.Infrastructure.Persistence;

internal sealed class SensitiveAttributeRow
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public string Name { get; set; } = "";
    public int Sensitivity { get; set; } = 1;
    public byte[] Envelope { get; set; } = [];
}
