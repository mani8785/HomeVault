using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Archives stored Vaults with current access checks inside a SQLite write transaction.</summary>
public sealed class SqliteVaultArchiveStore : IVaultArchiveStore
{
    private readonly SqliteDatabase _database;

    /// <summary>Configures storage without opening or migrating it.</summary>
    /// <param name="database">Explicit existing database configuration.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    public SqliteVaultArchiveStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public async Task<ArchiveVaultOutcome> ArchiveAsync(Guid vaultId, Guid actorId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (vaultId == Guid.Empty || actorId == Guid.Empty) throw new ArgumentException("Non-empty identities are required.");
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var access = await (from vault in context.Vaults
                            join member in context.Memberships on vault.Id equals member.VaultId
                            where vault.Id == vaultId && member.ActorId == actorId
                            select new { member.Role }).SingleOrDefaultAsync(cancellationToken);
        if (access is null) return ArchiveVaultOutcome.Unavailable;
        if (access.Role != (int)VaultRole.Owner) return ArchiveVaultOutcome.Forbidden;
        var row = await context.Vaults.SingleAsync(vault => vault.Id == vaultId, cancellationToken);
        var members = await context.Memberships.Where(member => member.VaultId == vaultId).ToListAsync(cancellationToken);
        var restored = Vault.Restore(row.Id, row.Name, (VaultType)row.Type, (VaultStatus)row.Status,
            members.Select(member => new KeyValuePair<Guid, VaultRole>(member.ActorId, (VaultRole)member.Role)));
        if (restored.Archive(actorId) != VaultArchiveError.None)
            throw new InvalidOperationException("Unexpected archive outcome.");
        row.Status = (int)restored.Status;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ArchiveVaultOutcome.Archived;
    }
}
