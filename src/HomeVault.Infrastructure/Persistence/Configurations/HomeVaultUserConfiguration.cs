using HomeVault.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeVault.Infrastructure.Persistence.Configurations;

internal sealed class HomeVaultUserConfiguration : IEntityTypeConfiguration<HomeVaultUser>
{
    public void Configure(EntityTypeBuilder<HomeVaultUser> entity)
    {
        entity.ToTable("AspNetUsers", table =>
            table.HasCheckConstraint("CK_User_Id", "Id <> '00000000-0000-0000-0000-000000000000'"));
        entity.Property(user => user.Id).ValueGeneratedNever();
    }
}
