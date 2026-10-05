using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeVault.Infrastructure.Persistence.Configurations;

internal sealed class RelationshipRowConfiguration : IEntityTypeConfiguration<RelationshipRow>
{
    public void Configure(EntityTypeBuilder<RelationshipRow> entity)
    {
        entity.ToTable("Relationships", table =>
        {
            table.HasCheckConstraint("CK_Relationship_Id", "Id <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Relationship_Kind", "Kind = 0");
            table.HasCheckConstraint("CK_Relationship_Status", "Status IN (0, 1)");
            table.HasCheckConstraint("CK_Relationship_DistinctEndpoints", "SourceAssetId <> TargetAssetId");
        });
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.HasOne<VaultRow>().WithMany().HasForeignKey(row => row.VaultId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<AssetRow>().WithMany().HasForeignKey(row => row.SourceAssetId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<AssetRow>().WithMany().HasForeignKey(row => row.TargetAssetId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(row => new { row.VaultId, row.SourceAssetId, row.TargetAssetId, row.Kind }).IsUnique().HasFilter("Status = 0");
    }
}
