namespace HomeVault.Infrastructure.Persistence;

internal sealed class RelationshipRow
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public Guid SourceAssetId { get; set; }
    public Guid TargetAssetId { get; set; }
    public int Kind { get; set; }
    public int Status { get; set; }
}
