using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Encryption;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

internal sealed class SqliteSensitiveAttributeStore(SqliteDatabase database, SensitiveStorageSession session, Action? testBeforeCommit = null) : ISensitiveAttributeStore
{
    public Task<SensitiveAttributeResult> AddAsync(Guid assetId, Guid actorId, string? name, string? value, AttributeSensitivity sensitivity, CancellationToken token) => Execute(assetId, actorId, Guid.Empty, name, value, sensitivity, Operation.Add, token);
    public Task<SensitiveAttributeResult> ChangeAsync(Guid assetId, Guid actorId, Guid attributeId, string? value, CancellationToken token) => Execute(assetId, actorId, attributeId, null, value, AttributeSensitivity.Sensitive, Operation.Change, token);
    public Task<SensitiveAttributeResult> RemoveAsync(Guid assetId, Guid actorId, Guid attributeId, CancellationToken token) => Execute(assetId, actorId, attributeId, null, null, AttributeSensitivity.Sensitive, Operation.Remove, token);
    public Task<SensitiveAttributeResult> ReadAsync(Guid assetId, Guid actorId, Guid attributeId, CancellationToken token) => Execute(assetId, actorId, attributeId, null, null, AttributeSensitivity.Sensitive, Operation.Read, token);
    public Task<SensitiveAttributeResult> ListAsync(Guid assetId, Guid actorId, CancellationToken token) => Execute(assetId, actorId, Guid.Empty, null, null, AttributeSensitivity.Sensitive, Operation.List, token);
    private enum Operation { Add, Change, Remove, Read, List }
    private static SensitiveAttributeResult Status(SensitiveAttributeOutcome outcome) => SensitiveAttributeResult.Status(outcome);

    private async Task<SensitiveAttributeResult> Execute(Guid assetId, Guid actorId, Guid attributeId, string? name, string? value, AttributeSensitivity sensitivity, Operation operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (assetId == Guid.Empty || actorId == Guid.Empty) return Status(SensitiveAttributeOutcome.Unavailable);
        var write = operation is Operation.Add or Operation.Change or Operation.Remove;
        await using var context = database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, token);
        await context.Database.OpenConnectionAsync(token);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: !write);
        await context.Database.UseTransactionAsync(transaction, token);
        var access = await (from asset in context.Assets.AsNoTracking()
                            join vault in context.Vaults on asset.VaultId equals vault.Id
                            join member in context.Memberships on vault.Id equals member.VaultId
                            where asset.Id == assetId && member.ActorId == actorId
                            select new { asset.VaultId, member.Role, vault.Status }).SingleOrDefaultAsync(token);
        if (access is null) return Status(SensitiveAttributeOutcome.Unavailable);
        if (!Enum.IsDefined((VaultRole)access.Role) || !Enum.IsDefined((VaultStatus)access.Status)) throw new InvalidOperationException("Invalid stored access state.");
        if (operation != Operation.List && access.Role is not ((int)VaultRole.Owner) and not ((int)VaultRole.Administrator)) return Status(SensitiveAttributeOutcome.Forbidden);
        if (write && access.Status == (int)VaultStatus.Archived) return Status(SensitiveAttributeOutcome.Archived);
        var metadata = await AttributeMetadata.Load(context, assetId, token);
        if (operation == Operation.List) return SensitiveAttributeResult.Listed(metadata.Where(row => row.Id.HasValue).Select(row => new SensitiveAttributeMetadata(row.Id!.Value, row.Name)));
        var target = metadata.SingleOrDefault(row => row.Id == attributeId);
        if (operation != Operation.Add && target is null) return Status(SensitiveAttributeOutcome.Unavailable);
        if (operation == Operation.Read)
        {
            var envelope = await context.SensitiveAttributes.Where(row => row.Id == attributeId && row.AssetId == assetId && row.Sensitivity == 1).Select(row => row.Envelope).SingleAsync(token);
            var decrypted = session.Decrypt(new EncryptionContext(access.VaultId, assetId, attributeId), envelope);
            return decrypted.Succeeded ? SensitiveAttributeResult.Read(decrypted.ReadValue()) : Status(SensitiveAttributeOutcome.SensitiveUnavailable);
        }
        if (operation == Operation.Remove)
        {
            if (await context.SensitiveAttributes.Where(row => row.Id == attributeId && row.AssetId == assetId && row.Sensitivity == 1).ExecuteDeleteAsync(token) != 1) throw new InvalidOperationException("Invalid stored attribute state.");
        }
        else
        {
            var candidate = AssetAttribute.Create(operation == Operation.Add ? name : target!.Name, value,
                sensitivity == AttributeSensitivity.Sensitive ? sensitivity : (AttributeSensitivity)(-1));
            if (candidate.Attribute is not { } entry) return Status(candidate.Error switch
            {
                AssetAttributeError.BlankName => SensitiveAttributeOutcome.InvalidName,
                AssetAttributeError.BlankValue => SensitiveAttributeOutcome.InvalidValue,
                _ => SensitiveAttributeOutcome.UnsupportedSensitivity
            });
            if (operation == Operation.Add && metadata.Any(row => StringComparer.OrdinalIgnoreCase.Equals(row.Name, entry.Name))) return Status(SensitiveAttributeOutcome.DuplicateName);
            if (operation == Operation.Add) attributeId = Guid.NewGuid();
            var encrypted = session.Encrypt(new EncryptionContext(access.VaultId, assetId, attributeId), entry.ReadValue());
            if (!encrypted.Succeeded) return Status(encrypted.Error is EncryptionError.InvalidText or EncryptionError.TooLarge ? SensitiveAttributeOutcome.InvalidValue : SensitiveAttributeOutcome.SensitiveUnavailable);
            if (operation == Operation.Add)
            {
                context.SensitiveAttributes.Add(new SensitiveAttributeRow { Id = attributeId, AssetId = assetId, Name = entry.Name, Envelope = encrypted.ReadValue() });
                await context.SaveChangesAsync(token);
            }
            else if (await context.SensitiveAttributes.Where(row => row.Id == attributeId && row.AssetId == assetId && row.Sensitivity == 1).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Envelope, encrypted.ReadValue()), token) != 1)
                throw new InvalidOperationException("Invalid stored attribute state.");
        }
        testBeforeCommit?.Invoke();
        await transaction.CommitAsync(token);
        return operation == Operation.Add ? SensitiveAttributeResult.Added(attributeId) : Status(SensitiveAttributeOutcome.Succeeded);
    }
}
