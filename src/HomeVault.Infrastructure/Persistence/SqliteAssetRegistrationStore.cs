using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Registers an Asset with access checks and insertion in one SQLite write transaction.</summary>
public sealed class SqliteAssetRegistrationStore : IAssetRegistrationStore
{
    private readonly SqliteDatabase _database;

    /// <summary>Constructs the adapter without opening or migrating storage.</summary>
    /// <param name="database">Explicit local database configuration.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    public SqliteAssetRegistrationStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public async Task<AssetRegistrationOutcome> RegisterAsync(Asset asset, Guid actorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        cancellationToken.ThrowIfCancellationRequested();
        if (actorId == Guid.Empty) throw new ArgumentException("A non-empty actor is required.", nameof(actorId));
        if (asset.VaultId is not { } vaultId || asset.Attributes.Count != 0 || asset.Evidence.Count != 0)
            throw new ArgumentException("Expected a bound Asset without attributes or Evidence.", nameof(asset));
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var access = await (from vault in context.Vaults
                            join member in context.Memberships on vault.Id equals member.VaultId
                            where vault.Id == vaultId && member.ActorId == actorId
                            select new { vault.Status, member.Role }).SingleOrDefaultAsync(cancellationToken);
        if (access is null) return AssetRegistrationOutcome.VaultUnavailable;
        if (access.Role is not ((int)VaultRole.Owner or (int)VaultRole.Administrator or (int)VaultRole.Editor))
            return AssetRegistrationOutcome.Forbidden;
        if (access.Status != (int)VaultStatus.Active) return AssetRegistrationOutcome.VaultArchived;
        // The write reservation makes this duplicate check and insert indivisible to other writers.
        if (await context.Assets.AnyAsync(row => row.Id == asset.Id, cancellationToken))
            return AssetRegistrationOutcome.IdentityConflict;
        context.Assets.Add(new AssetRow { Id = asset.Id, VaultId = vaultId, Name = asset.Name });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AssetRegistrationOutcome.Added;
    }
}
