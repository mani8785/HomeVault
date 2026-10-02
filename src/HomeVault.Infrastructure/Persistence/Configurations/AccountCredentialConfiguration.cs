using HomeVault.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeVault.Infrastructure.Persistence.Configurations;

internal sealed class AccountCredentialConfiguration : IEntityTypeConfiguration<AccountCredentialRow>
{
    public void Configure(EntityTypeBuilder<AccountCredentialRow> builder)
    {
        builder.ToTable("AccountCredentials");
        builder.HasKey(row => row.Hash);
        builder.Property(row => row.Hash).HasMaxLength(64);
        builder.Property(row => row.Login).HasMaxLength(256).IsRequired();
        builder.Property(row => row.Purpose).HasMaxLength(16).IsRequired();
        builder.HasIndex(row => new { row.Login, row.Purpose });
    }
}
