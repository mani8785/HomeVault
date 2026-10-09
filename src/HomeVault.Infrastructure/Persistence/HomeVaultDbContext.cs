using HomeVault.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Maps storage-only records to the local SQLite database.</summary>
/// <param name="options">Explicit provider options; each operation uses a separate context.</param>
public sealed class HomeVaultDbContext(DbContextOptions<HomeVaultDbContext> options)
    : IdentityDbContext<HomeVaultUser, IdentityRole<Guid>, Guid>(options)
{
    internal DbSet<VaultRow> Vaults => Set<VaultRow>();
    internal DbSet<MembershipRow> Memberships => Set<MembershipRow>();
    internal DbSet<AssetRow> Assets => Set<AssetRow>();
    internal DbSet<AssetAttributeRow> AssetAttributes => Set<AssetAttributeRow>();
    internal DbSet<SensitiveAttributeRow> SensitiveAttributes => Set<SensitiveAttributeRow>();
    internal DbSet<EvidenceRow> AssetEvidence => Set<EvidenceRow>();
    internal DbSet<RelationshipRow> Relationships => Set<RelationshipRow>();
    internal DbSet<ReminderRow> Reminders => Set<ReminderRow>();

    /// <summary>Applies the storage model configurations defined in Infrastructure.</summary>
    /// <param name="modelBuilder">The EF model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HomeVaultDbContext).Assembly);
    }
}
