using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeVault.Infrastructure.Persistence.Configurations;

internal sealed class SensitiveAttributeRowConfiguration : IEntityTypeConfiguration<SensitiveAttributeRow>
{
    public void Configure(EntityTypeBuilder<SensitiveAttributeRow> entity)
    {
        entity.ToTable("SensitiveAssetAttributes", table =>
        {
            table.HasCheckConstraint("CK_SensitiveAttribute_Classification", "Sensitivity = 1");
            table.HasCheckConstraint("CK_SensitiveAttribute_Identity", "Id <> '00000000-0000-0000-0000-000000000000'");
        });
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Name).IsRequired().UseCollation("BINARY");
        entity.Property(row => row.Envelope).IsRequired();
        entity.HasIndex(row => new { row.AssetId, row.Name }).IsUnique();
        entity.HasOne<AssetRow>().WithMany().HasForeignKey(row => row.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
