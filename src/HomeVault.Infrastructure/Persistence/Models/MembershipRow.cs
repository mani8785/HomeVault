namespace HomeVault.Infrastructure.Persistence;

internal sealed class MembershipRow
{
    public Guid VaultId { get; set; }
    public Guid ActorId { get; set; }
    public int Role { get; set; }
}
