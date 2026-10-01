using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeVault.Infrastructure.Persistence.Configurations;

internal sealed class VaultRowConfiguration : IEntityTypeConfiguration<VaultRow>
{
    public void Configure(EntityTypeBuilder<VaultRow> entity)
    {
        entity.ToTable("Vaults", table =>
        {
            table.HasCheckConstraint("CK_Vault_Type", "Type IN (0, 1, 2)");
            table.HasCheckConstraint("CK_Vault_Status", "Status IN (0, 1)");
            table.HasCheckConstraint("CK_Vault_Id", "Id <> '00000000-0000-0000-0000-000000000000'");
        });
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Name).IsRequired();
    }
}
