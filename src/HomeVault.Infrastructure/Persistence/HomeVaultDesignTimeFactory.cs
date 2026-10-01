using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Creates migration tooling contexts without selecting a user's database implicitly.</summary>
public sealed class HomeVaultDesignTimeFactory : IDesignTimeDbContextFactory<HomeVaultDbContext>
{
    /// <summary>Creates a tooling context from HOMEVAULT_DATABASE or an in-memory design-only database.</summary>
    /// <param name="args">EF tooling arguments.</param>
    /// <returns>A SQLite context. Migration generation does not modify a user database.</returns>
    public HomeVaultDbContext CreateDbContext(string[] args)
    {
        var path = Environment.GetEnvironmentVariable("HOMEVAULT_DATABASE");
        var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(path) ? ":memory:" : Path.GetFullPath(path),
            ForeignKeys = true
        };
        return new HomeVaultDbContext(new DbContextOptionsBuilder<HomeVaultDbContext>().UseSqlite(connection.ToString()).Options);
    }
}
