using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Atomically stores a new Vault and initial Owner in SQLite.</summary>
public sealed class SqliteVaultRepository : IVaultRepository
{
    private readonly SqliteDatabase _database;

    /// <summary>Constructs an adapter without opening or migrating the database.</summary>
    /// <param name="database">Explicit local database configuration.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    public SqliteVaultRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public async Task<VaultAddOutcome> AddAsync(Vault vault, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vault);
        cancellationToken.ThrowIfCancellationRequested();
        var members = vault.Memberships;
        if (vault.Status != VaultStatus.Active || members.Count != 1 || members[0].Role != VaultRole.Owner)
            throw new ArgumentException("Expected an Active Vault with one Owner.", nameof(vault));
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        // Acquire the SQLite write reservation before checking identity, across processes.
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (await context.Vaults.AnyAsync(row => row.Id == vault.Id, cancellationToken))
            return VaultAddOutcome.IdentityConflict;
        context.Vaults.Add(new VaultRow { Id = vault.Id, Name = vault.Name, Type = (int)vault.Type, Status = (int)vault.Status });
        context.Memberships.Add(new MembershipRow { VaultId = vault.Id, ActorId = members[0].ActorId, Role = (int)VaultRole.Owner });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return VaultAddOutcome.Added;
    }
}
