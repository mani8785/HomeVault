using HomeVault.Application.Records;
using HomeVault.Domain;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Renames records while serializing current access, archive and expected-name checks with writes.</summary>
public sealed class SqliteRecordNameStore : IRecordNameStore
{
    private readonly SqliteDatabase _database;

    /// <summary>Configures existing storage without opening or migrating it.</summary>
    /// <param name="database">Explicit SQLite configuration.</param>
    /// <exception cref="ArgumentNullException">Configuration is null.</exception>
    public SqliteRecordNameStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public Task<RenameOutcome> RenameVaultAsync(Guid vaultId, Guid actorId, string? name, string? expectedName, CancellationToken cancellationToken) =>
        Rename(vaultId, actorId, name, expectedName, false, cancellationToken);

    /// <inheritdoc />
    public Task<RenameOutcome> RenameAssetAsync(Guid assetId, Guid actorId, string? name, string? expectedName, CancellationToken cancellationToken) =>
        Rename(assetId, actorId, name, expectedName, true, cancellationToken);

    private async Task<RenameOutcome> Rename(Guid id, Guid actorId, string? name, string? expectedName, bool isAsset, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (id == Guid.Empty || actorId == Guid.Empty) throw new ArgumentException("Nonempty identities are required.");
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, token);
        await context.Database.OpenConnectionAsync(token);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await context.Database.UseTransactionAsync(transaction, token);
        var vaultId = isAsset
            ? await context.Assets.Where(row => row.Id == id).Select(row => (Guid?)row.VaultId).SingleOrDefaultAsync(token)
            : id;
        var access = await (from vault in context.Vaults
                            join member in context.Memberships on vault.Id equals member.VaultId
                            where vault.Id == vaultId && member.ActorId == actorId
                            select new { member.Role, vault.Status }).SingleOrDefaultAsync(token);
        if (access is null) return RenameOutcome.Unavailable;
        if (!Enum.IsDefined((VaultRole)access.Role) || !Enum.IsDefined((VaultStatus)access.Status))
            throw new InvalidOperationException("Invalid stored access state.");
        if (access.Role == (int)VaultRole.Viewer || !isAsset && access.Role == (int)VaultRole.Editor) return RenameOutcome.Forbidden;
        if (access.Status == (int)VaultStatus.Archived) return RenameOutcome.Archived;

        var vaultRow = await context.Vaults.SingleAsync(row => row.Id == vaultId, token);
        var memberships = await context.Memberships.Where(row => row.VaultId == vaultId).ToListAsync(token);
        var restoredVault = Vault.Restore(vaultRow.Id, vaultRow.Name, (VaultType)vaultRow.Type, (VaultStatus)vaultRow.Status,
            memberships.Select(row => KeyValuePair.Create(row.ActorId, (VaultRole)row.Role)));
        AssetRow? assetRow = null;
        Asset? asset = null;
        if (isAsset)
        {
            assetRow = await context.Assets.SingleAsync(row => row.Id == id, token);
            // Restore only the metadata needed here; no attribute values or Evidence content are loaded or saved.
            asset = Asset.RestoreOrdinaryAttributes(assetRow.Id, assetRow.VaultId, assetRow.Name, []);
        }
        var error = isAsset ? asset!.Rename(name) : restoredVault.Rename(name);
        if (error != RenameError.None) return RenameOutcome.InvalidName;
        // Use the same Domain validation for the expected name, without retaining it or echoing failures.
        try
        {
            _ = new AssetName(expectedName);
        }
        catch (ArgumentException exception) when (exception.ParamName == "name")
        {
            return RenameOutcome.InvalidExpectedName;
        }
        var currentName = isAsset ? assetRow!.Name : vaultRow.Name;
        if (!StringComparer.Ordinal.Equals(currentName, expectedName)) return RenameOutcome.Conflict;
        if (isAsset) assetRow!.Name = asset!.Name;
        else vaultRow.Name = restoredVault.Name;
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return RenameOutcome.Succeeded;
    }
}
