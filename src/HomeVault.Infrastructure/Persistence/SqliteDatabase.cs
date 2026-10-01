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

    /// <summary>Creates a verified backup or restored copy without replacing an existing destination.</summary>
    /// <param name="destinationPath">A new absolute local path; its parent directory must exist.</param>
    /// <param name="cancellationToken">Cancellation checked around the synchronous SQLite backup operation.</param>
    /// <returns>A task completing after integrity, foreign keys, and compatible schema history are verified and the copy is published.</returns>
    /// <exception cref="ArgumentException">The destination is not an absolute path.</exception>
    /// <exception cref="IOException">The destination exists or a file operation fails.</exception>
    /// <exception cref="InvalidOperationException">The copy has invalid integrity, foreign keys, or unsupported schema history.</exception>
    /// <remarks>
    /// Uses SQLite's backup API, never a live-file copy. The source is opened read-only.
    /// Stop writers before a restore or migration recovery; restore to a new location,
    /// verify expected records, and only then switch configuration. No encryption is provided.
    /// Failed validation removes only this operation's unpublished temporary copy.
    /// </remarks>
    public async Task CreateVerifiedCopyAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Path.IsPathFullyQualified(destinationPath)) throw new ArgumentException("An absolute destination is required.", nameof(destinationPath));
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(destinationPath)) throw new IOException("The destination already exists.");
        var temporaryPath = destinationPath + ".partial-" + Guid.NewGuid().ToString("N");
        var created = false;
        try
        {
            // Reserve a new staging file; never overwrite an existing file or publish before validation.
            using (new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            created = true;
            var sourceOptions = new SqliteConnectionStringBuilder(_connectionString) { Mode = SqliteOpenMode.ReadOnly };
            await using (var source = new SqliteConnection(sourceOptions.ToString()))
            await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Pooling = false,
                ForeignKeys = true
            }.ToString()))
            {
                await source.OpenAsync(cancellationToken);
                await destination.OpenAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                source.BackupDatabase(destination);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var staged = new SqliteDatabase(temporaryPath);
            await using (var context = staged.CreateContext())
            {
                await ValidateHistoryAsync(context, false, cancellationToken);
                if (!(await context.Database.GetAppliedMigrationsAsync(cancellationToken)).Any())
                    throw new InvalidOperationException("No recognized schema is present.");
                await context.Database.OpenConnectionAsync(cancellationToken);
                await using var integrity = context.Database.GetDbConnection().CreateCommand();
                integrity.CommandText = "PRAGMA integrity_check";
                if (!string.Equals(await integrity.ExecuteScalarAsync(cancellationToken) as string, "ok", StringComparison.Ordinal))
                    throw new InvalidOperationException("Database integrity validation failed.");
                await using var foreignKeys = context.Database.GetDbConnection().CreateCommand();
                foreignKeys.CommandText = "PRAGMA foreign_key_check";
                await using var violations = await foreignKeys.ExecuteReaderAsync(cancellationToken);
                if (await violations.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Database relationship validation failed.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath, overwrite: false);
            created = false;
        }
        finally
        {
            if (created) File.Delete(temporaryPath);
        }
    }

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
