using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Maps storage-only records to the local SQLite database.</summary>
/// <param name="options">Explicit provider options; each operation uses a separate context.</param>
public sealed class HomeVaultDbContext(DbContextOptions<HomeVaultDbContext> options) : DbContext(options)
{
    internal DbSet<VaultRow> Vaults => Set<VaultRow>();
    internal DbSet<MembershipRow> Memberships => Set<MembershipRow>();
    internal DbSet<AssetRow> Assets => Set<AssetRow>();

    /// <summary>Defines database keys, relationships, and supported state constraints.</summary>
    /// <param name="modelBuilder">The EF model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssetRow>(entity =>
        {
            entity.ToTable("Assets", table => table.HasCheckConstraint("CK_Asset_Id", "Id <> '00000000-0000-0000-0000-000000000000'"));
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Name).IsRequired();
            entity.HasOne<VaultRow>().WithMany().HasForeignKey(row => row.VaultId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<VaultRow>(entity =>
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
        });
        modelBuilder.Entity<MembershipRow>(entity =>
        {
            entity.ToTable("Memberships", table =>
            {
                table.HasCheckConstraint("CK_Membership_Role", "Role IN (0, 1, 2, 3)");
                table.HasCheckConstraint("CK_Membership_Actor", "ActorId <> '00000000-0000-0000-0000-000000000000'");
            });
            entity.HasKey(row => new { row.VaultId, row.ActorId });
            entity.HasOne<VaultRow>().WithMany().HasForeignKey(row => row.VaultId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class VaultRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public int Status { get; set; }
}

internal sealed class MembershipRow
{
    public Guid VaultId { get; set; }
    public Guid ActorId { get; set; }
    public int Role { get; set; }
}

internal sealed class AssetRow
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public string Name { get; set; } = "";
}
