using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Encryption;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

internal static class EncryptedDatabaseMaintenance
{
    internal static async Task Validate(SqliteDatabase database, IEncryptionKeyCustody custody, CancellationToken token)
    {
        await using var db = database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(db, true, token);
        await db.Database.OpenConnectionAsync(token);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check";
            await using var rows = await command.ExecuteReaderAsync(token);
            if (!await rows.ReadAsync(token) || rows.GetString(0) != "ok" || await rows.ReadAsync(token)) throw new InvalidOperationException();
        }
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check";
            await using var rows = await command.ExecuteReaderAsync(token);
            if (await rows.ReadAsync(token)) throw new InvalidOperationException();
        }
        if (await db.Users.AnyAsync(user => user.Id == Guid.Empty || user.SecurityStamp == null || user.SecurityStamp == "", token) ||
            await db.Memberships.AnyAsync(member => !db.Users.Any(user => user.Id == member.ActorId), token)) throw new InvalidOperationException();
        foreach (var vault in await db.Vaults.AsNoTracking().ToListAsync(token))
        {
            var members = await db.Memberships.AsNoTracking().Where(row => row.VaultId == vault.Id).ToListAsync(token);
            _ = Vault.Restore(vault.Id, vault.Name, (VaultType)vault.Type, (VaultStatus)vault.Status,
                members.Select(row => new KeyValuePair<Guid, VaultRole>(row.ActorId, (VaultRole)row.Role)));
        }
        var encryption = new EnvelopeEncryption(custody);
        var offset = 0;
        while (true)
        {
            var assets = await db.Assets.AsNoTracking().OrderBy(row => row.Id).Skip(offset).Take(128).ToListAsync(token);
            if (assets.Count == 0) break;
            foreach (var asset in assets)
            {
                _ = await AttributeMetadata.Load(db, asset.Id, token);
                var ordinary = await db.AssetAttributes.AsNoTracking().Where(row => row.AssetId == asset.Id).ToListAsync(token);
                _ = Asset.RestoreOrdinaryAttributes(asset.Id, asset.VaultId, asset.Name, ordinary.Select(row => new KeyValuePair<string, string>(row.Name, row.Value)));
                var evidence = await db.AssetEvidence.AsNoTracking().Where(row => row.AssetId == asset.Id).ToListAsync(token);
                _ = Asset.RestoreEvidence(asset.Id, asset.VaultId, asset.Name, evidence.Select(row => (row.Id, row.Label, (EvidenceKind)row.Kind, row.Content)));
                foreach (var row in await db.Reminders.AsNoTracking().Where(row => row.AssetId == asset.Id).ToListAsync(token))
                    _ = await SqliteReminderStore.Restore(db, row, token);
                foreach (var row in await db.Relationships.AsNoTracking().Where(row => row.SourceAssetId == asset.Id).ToListAsync(token))
                    _ = await SqliteRelationshipStore.Restore(db, row, token);
                await foreach (var row in db.SensitiveAttributes.AsNoTracking().Where(row => row.AssetId == asset.Id).AsAsyncEnumerable().WithCancellation(token))
                {
                    var value = encryption.Decrypt(new EncryptionContext(asset.VaultId, asset.Id, row.Id), row.Envelope).ReadValue();
                    if (AssetAttribute.Create(row.Name, value, AttributeSensitivity.Sensitive).Attribute is null) throw new InvalidOperationException();
                }
            }
            offset = checked(offset + assets.Count);
        }
    }

    internal static async Task Rotate(SqliteDatabase database, IEncryptionKeyCustody custody, Func<WriteKeySession?> start,
        CancellationToken token, Action<string>? checkpoint = null, int sessionLimit = WriteKeySession.ReservationLimit)
    {
        if (sessionLimit is < 1 or > WriteKeySession.ReservationLimit) throw new InvalidOperationException();
        await Validate(database, custody, token);
        var encryption = new EnvelopeEncryption(custody);
        WriteKeySession? session = null;
        var used = 0; var offset = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                await using var db = database.CreateContext();
                await db.Database.OpenConnectionAsync(token);
                await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
                await db.Database.UseTransactionAsync(transaction, token);
                var rows = await db.SensitiveAttributes.OrderBy(row => row.Id).Skip(offset).Take(128).ToListAsync(token);
                if (rows.Count == 0) break;
                foreach (var row in rows)
                {
                    if (session is null || used == sessionLimit)
                    {
                        session?.Dispose(); session = start() ?? throw new InvalidOperationException(); used = 0;
                    }
                    var vault = await db.Assets.Where(asset => asset.Id == row.AssetId).Select(asset => asset.VaultId).SingleAsync(token);
                    var binding = new EncryptionContext(vault, row.AssetId, row.Id);
                    row.Envelope = session.Encrypt(binding, encryption.Decrypt(binding, row.Envelope).ReadValue()).ReadValue();
                    used++;
                }
                await db.SaveChangesAsync(token); checkpoint?.Invoke("batch-written");
                await transaction.CommitAsync(token); checkpoint?.Invoke("batch-committed");
                offset = checked(offset + rows.Count);
            }
        }
        finally { session?.Dispose(); }
        await Validate(database, custody, token);
    }
}
