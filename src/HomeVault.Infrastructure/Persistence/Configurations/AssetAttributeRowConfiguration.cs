using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeVault.Infrastructure.Persistence.Configurations;

internal sealed class AssetAttributeRowConfiguration : IEntityTypeConfiguration<AssetAttributeRow>
{
    public void Configure(EntityTypeBuilder<AssetAttributeRow> entity)
    {
        entity.ToTable("AssetAttributes", table => table.HasCheckConstraint("CK_AssetAttribute_Ordinary", "Sensitivity = 0"));
        entity.HasKey(row => new { row.AssetId, row.Name });
        entity.Property(row => row.Name).IsRequired().UseCollation("BINARY");
        entity.Property(row => row.Value).IsRequired();
        entity.HasOne<AssetRow>().WithMany().HasForeignKey(row => row.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
