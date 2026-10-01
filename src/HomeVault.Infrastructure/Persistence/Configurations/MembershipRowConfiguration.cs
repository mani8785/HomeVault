using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeVault.Infrastructure.Persistence.Configurations;

internal sealed class MembershipRowConfiguration : IEntityTypeConfiguration<MembershipRow>
{
    public void Configure(EntityTypeBuilder<MembershipRow> entity)
    {
        entity.ToTable("Memberships", table =>
        {
            table.HasCheckConstraint("CK_Membership_Role", "Role IN (0, 1, 2, 3)");
            table.HasCheckConstraint("CK_Membership_Actor", "ActorId <> '00000000-0000-0000-0000-000000000000'");
        });
        entity.HasKey(row => new { row.VaultId, row.ActorId });
        entity.HasOne<VaultRow>().WithMany().HasForeignKey(row => row.VaultId).OnDelete(DeleteBehavior.Restrict);
    }
}
