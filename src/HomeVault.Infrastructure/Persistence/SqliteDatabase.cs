using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Creates short-lived SQLite contexts and exposes explicit schema operations.</summary>
public sealed class SqliteDatabase
{
    private readonly string _connectionString;

    /// <summary>Configures an existing parent directory and an absolute local database file path.</summary>
    /// <param name="path">An absolute path outside the source checkout; callers manage directory creation.</param>
    /// <exception cref="ArgumentException">The path is blank or not fully qualified.</exception>
    public SqliteDatabase(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute database path is required.", nameof(path));
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    internal HomeVaultDbContext CreateContext() => new(new DbContextOptionsBuilder<HomeVaultDbContext>().UseSqlite(_connectionString).Options);

    /// <summary>Explicitly upgrades a compatible database; never called automatically by application operations.</summary>
    /// <param name="cancellationToken">Cancellation for migration operations.</param>
    /// <returns>A task completing after migrations have been applied.</returns>
    /// <exception cref="InvalidOperationException">The database has an unknown or non-prefix migration history.</exception>
    /// <remarks>Stop application writers and back up first. Provider failures propagate without private diagnostic logging.</remarks>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext();
        await ValidateHistoryAsync(context, false, cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }

    internal static async Task ValidateHistoryAsync(HomeVaultDbContext context, bool requireCurrent, CancellationToken token)
    {
        var known = context.Database.GetMigrations().ToArray();
        var applied = (await context.Database.GetAppliedMigrationsAsync(token)).ToArray();
        if (!applied.SequenceEqual(known.Take(applied.Length)) || applied.Length > known.Length ||
            (requireCurrent && applied.Length != known.Length))
            throw new InvalidOperationException("Database schema is unsupported or requires explicit migration.");
    }
}
