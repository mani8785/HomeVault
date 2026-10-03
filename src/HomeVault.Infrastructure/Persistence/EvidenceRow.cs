namespace HomeVault.Infrastructure.Persistence;

internal sealed class EvidenceRow
{
    public Guid AssetId { get; set; }
    public Guid Id { get; set; }
    public string Label { get; set; } = "";
    public int Kind { get; set; }
    public string Content { get; set; } = "";
}
