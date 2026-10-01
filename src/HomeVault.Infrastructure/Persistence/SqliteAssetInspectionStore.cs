using HomeVault.Application.Assets;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Returns Asset metadata through one membership-scoped SQL query.</summary>
public sealed class SqliteAssetInspectionStore : IAssetInspectionStore
{
    private readonly SqliteDatabase _database;

    /// <summary>Constructs the adapter without opening or migrating the database.</summary>
    /// <param name="database">Explicit local storage configuration.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    public SqliteAssetInspectionStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public async Task<InspectedAsset?> FindAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (assetId == Guid.Empty || actorId == Guid.Empty) return null;
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, cancellationToken);
        // One SQL statement supplies a consistent view of record and permission.
        return await (from asset in context.Assets.AsNoTracking()
                      join member in context.Memberships on asset.VaultId equals member.VaultId
                      where asset.Id == assetId && member.ActorId == actorId
                      select new InspectedAsset(asset.Id, asset.VaultId, asset.Name)).SingleOrDefaultAsync(cancellationToken);
    }
}
