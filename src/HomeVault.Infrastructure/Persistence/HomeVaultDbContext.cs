using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Maps storage-only records to the local SQLite database.</summary>
/// <param name="options">Explicit provider options; each operation uses a separate context.</param>
public sealed class HomeVaultDbContext(DbContextOptions<HomeVaultDbContext> options) : DbContext(options)
{
    internal DbSet<VaultRow> Vaults => Set<VaultRow>();
    internal DbSet<MembershipRow> Memberships => Set<MembershipRow>();
    internal DbSet<AssetRow> Assets => Set<AssetRow>();

    /// <summary>Applies the storage model configurations defined in Infrastructure.</summary>
    /// <param name="modelBuilder">The EF model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HomeVaultDbContext).Assembly);
    }
}
