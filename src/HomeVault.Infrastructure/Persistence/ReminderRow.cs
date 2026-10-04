namespace HomeVault.Infrastructure.Persistence;

internal sealed class ReminderRow
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public Guid AssetId { get; set; }
    public string Action { get; set; } = null!;
    public long DueAtUtcTicks { get; set; }
    public int Status { get; set; }
}
