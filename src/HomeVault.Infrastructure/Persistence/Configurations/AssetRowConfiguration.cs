using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeVault.Infrastructure.Persistence.Configurations;

internal sealed class AssetRowConfiguration : IEntityTypeConfiguration<AssetRow>
{
    public void Configure(EntityTypeBuilder<AssetRow> entity)
    {
        entity.ToTable("Assets", table => table.HasCheckConstraint("CK_Asset_Id", "Id <> '00000000-0000-0000-0000-000000000000'"));
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Name).IsRequired();
        entity.HasOne<VaultRow>().WithMany().HasForeignKey(row => row.VaultId).OnDelete(DeleteBehavior.Restrict);
    }
}
