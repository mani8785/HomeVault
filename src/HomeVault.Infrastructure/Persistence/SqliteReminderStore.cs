using HomeVault.Application.Reminders;
using HomeVault.Domain.Reminders;
using HomeVault.Domain.Vaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Serializes current access, actual ownership and Reminder lifecycle writes in SQLite transactions.</summary>
public sealed class SqliteReminderStore : IReminderStore
{
    private readonly SqliteDatabase _database;
    /// <summary>Configures storage without opening or migrating the database.</summary>
    /// <param name="database">Existing database configuration.</param>
    /// <exception cref="ArgumentNullException">Configuration is null.</exception>
    public SqliteReminderStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database); _database = database;
    }
    /// <inheritdoc />
    public Task<ReminderOutcome> CreateAsync(Guid vaultId, Guid actorId, Guid id, Guid assetId, string? action, DateTimeOffset dueAt, CancellationToken token) =>
        Mutate(vaultId, actorId, id, assetId, action, dueAt, Operation.Create, token);
    /// <inheritdoc />
    public Task<ReminderOutcome> UpdateAsync(Guid vaultId, Guid actorId, Guid id, string? action, DateTimeOffset dueAt, CancellationToken token) =>
        Mutate(vaultId, actorId, id, Guid.Empty, action, dueAt, Operation.Update, token);
    /// <inheritdoc />
    public Task<ReminderOutcome> CompleteAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token) =>
        Mutate(vaultId, actorId, id, Guid.Empty, null, default, Operation.Complete, token);
    /// <inheritdoc />
    public Task<ReminderOutcome> CancelAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token) =>
        Mutate(vaultId, actorId, id, Guid.Empty, null, default, Operation.Cancel, token);
    /// <inheritdoc />
    public async Task<ReminderSnapshot?> FindAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (vaultId == Guid.Empty || actorId == Guid.Empty || id == Guid.Empty) return null;
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, token);
        await context.Database.OpenConnectionAsync(token);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, token);
        var access = await Access(context, vaultId, actorId).SingleOrDefaultAsync(token);
        if (access is null) return null;
        ValidateAccess(access);
        var row = await context.Reminders.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.VaultId == vaultId, token);
        if (row is null) return null;
        var root = await Restore(context, row, token);
        return new ReminderSnapshot(root.Id, root.VaultId, root.AssetId, root.DueAt, root.Status, root.ReadAction());
    }
    private async Task<ReminderOutcome> Mutate(Guid vaultId, Guid actorId, Guid id, Guid assetId, string? action, DateTimeOffset dueAt, Operation operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (vaultId == Guid.Empty || actorId == Guid.Empty || id == Guid.Empty || (operation == Operation.Create && assetId == Guid.Empty))
            throw new ArgumentException("Valid identities are required.");
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, token);
        await context.Database.OpenConnectionAsync(token);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await context.Database.UseTransactionAsync(transaction, token);
        var access = await Access(context, vaultId, actorId).SingleOrDefaultAsync(token);
        if (access is null) return ReminderOutcome.Unavailable;
        ValidateAccess(access);
        if (access.Role == (int)VaultRole.Viewer) return ReminderOutcome.Forbidden;
        if (access.Status == (int)VaultStatus.Archived) return ReminderOutcome.Archived;
        if (operation == Operation.Create)
        {
            if (!await OwnsAsset(context, vaultId, assetId, token)) return ReminderOutcome.Unavailable;
            var created = Reminder.Create(id, vaultId, assetId, action, dueAt);
            if (created.Reminder is not { } root) return Outcome(created.Error);
            var existing = await context.Reminders.Where(row => row.Id == id).Select(row => (Guid?)row.VaultId).SingleOrDefaultAsync(token);
            if (existing.HasValue) return existing.Value == vaultId ? ReminderOutcome.IdentityConflict : ReminderOutcome.Unavailable;
            context.Reminders.Add(new ReminderRow
            {
                Id = root.Id,
                VaultId = root.VaultId,
                AssetId = root.AssetId,
                Action = root.ReadAction(),
                DueAtUtcTicks = root.DueAt.UtcTicks,
                Status = (int)root.Status
            });
        }
        else
        {
            var row = await context.Reminders.SingleOrDefaultAsync(row => row.Id == id && row.VaultId == vaultId, token);
            if (row is null) return ReminderOutcome.Unavailable;
            var root = await Restore(context, row, token);
            var error = operation switch { Operation.Update => root.Update(action, dueAt), Operation.Complete => root.Complete(), _ => root.Cancel() };
            if (error != ReminderError.None) return Outcome(error);
            row.Action = root.ReadAction(); row.DueAtUtcTicks = root.DueAt.UtcTicks; row.Status = (int)root.Status;
        }
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return ReminderOutcome.Succeeded;
    }
    private static ReminderOutcome Outcome(ReminderError error) => error switch
    {
        ReminderError.BlankAction => ReminderOutcome.BlankAction,
        ReminderError.NotPending => ReminderOutcome.NotPending,
        _ => throw new InvalidOperationException("Unexpected Reminder outcome.")
    };
    internal static async Task<Reminder> Restore(HomeVaultDbContext context, ReminderRow row, CancellationToken token)
    {
        if (row.DueAtUtcTicks < 0 || row.DueAtUtcTicks > DateTimeOffset.MaxValue.UtcTicks)
            throw new InvalidOperationException("Invalid stored Reminder instant.");
        var root = Reminder.Restore(row.Id, row.VaultId, row.AssetId, row.Action, new DateTimeOffset(row.DueAtUtcTicks, TimeSpan.Zero), (ReminderStatus)row.Status);
        if (!await OwnsAsset(context, root.VaultId, root.AssetId, token)) throw new InvalidOperationException("Invalid stored Reminder ownership.");
        return root;
    }
    private static Task<bool> OwnsAsset(HomeVaultDbContext context, Guid vaultId, Guid assetId, CancellationToken token) =>
        context.Assets.AnyAsync(asset => asset.Id == assetId && asset.VaultId == vaultId, token);
    private static IQueryable<AccessState> Access(HomeVaultDbContext context, Guid vaultId, Guid actorId) =>
        from vault in context.Vaults.AsNoTracking()
        join member in context.Memberships on vault.Id equals member.VaultId
        where vault.Id == vaultId && member.ActorId == actorId
        select new AccessState { Role = member.Role, Status = vault.Status };
    private static void ValidateAccess(AccessState access)
    {
        if (!Enum.IsDefined((VaultRole)access.Role) || !Enum.IsDefined((VaultStatus)access.Status))
            throw new InvalidOperationException("Invalid stored access state.");
    }
    private sealed class AccessState { public int Role { get; init; } public int Status { get; init; } }
    private enum Operation { Create, Update, Complete, Cancel }
}
