using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeVault.Infrastructure.Persistence.Configurations;

internal sealed class ReminderRowConfiguration : IEntityTypeConfiguration<ReminderRow>
{
    public void Configure(EntityTypeBuilder<ReminderRow> entity)
    {
        entity.ToTable("Reminders", table =>
        {
            table.HasCheckConstraint("CK_Reminder_Id", "Id <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Reminder_Status", "Status IN (0, 1, 2)");
            table.HasCheckConstraint("CK_Reminder_DueAt", "typeof(DueAtUtcTicks) = 'integer' AND DueAtUtcTicks BETWEEN 0 AND 3155378975999999999");
        });
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Action).IsRequired();
        entity.HasOne<VaultRow>().WithMany().HasForeignKey(row => row.VaultId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<AssetRow>().WithMany().HasForeignKey(row => row.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
