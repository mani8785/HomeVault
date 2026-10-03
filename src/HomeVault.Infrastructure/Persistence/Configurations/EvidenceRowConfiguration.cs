using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeVault.Infrastructure.Persistence.Configurations;

internal sealed class EvidenceRowConfiguration : IEntityTypeConfiguration<EvidenceRow>
{
    public void Configure(EntityTypeBuilder<EvidenceRow> entity)
    {
        entity.ToTable("AssetEvidence", table =>
        {
            table.HasCheckConstraint("CK_Evidence_Id", "Id <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Evidence_Kind", "Kind IN (0, 1)");
        });
        entity.HasKey(row => new { row.AssetId, row.Id });
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Label).IsRequired();
        entity.Property(row => row.Content).IsRequired();
        entity.HasOne<AssetRow>().WithMany().HasForeignKey(row => row.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
