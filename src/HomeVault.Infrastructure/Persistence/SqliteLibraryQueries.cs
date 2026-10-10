using HomeVault.Application.Library;
using HomeVault.Domain.Vaults;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Projects bounded library metadata under current membership in a consistent read transaction.</summary>
/// <param name="database">Existing explicitly migrated SQLite database.</param>
public sealed class SqliteLibraryQueries(SqliteDatabase database) : ILibraryQueries
{
    /// <inheritdoc />
    public Task<LibraryPage<LibraryVault>?> VaultsAsync(Guid actor, LibraryQuery query, CancellationToken token) =>
        ReadAsync(actor, null, false, query, context =>
            from vault in context.Vaults.AsNoTracking()
            join member in context.Memberships on vault.Id equals member.VaultId
            where member.ActorId == actor && vault.Name.ToLower().Contains(query.Search.ToLower())
            orderby vault.Id
            select new LibraryVault(vault.Id, vault.Name, vault.Type, vault.Status, member.Role), token);

    /// <inheritdoc />
    public Task<LibraryPage<LibraryAsset>?> AssetsAsync(Guid actor, Guid vault, LibraryQuery query, CancellationToken token) =>
        ReadAsync(actor, vault, false, query, context => context.Assets.AsNoTracking()
            .Where(row => row.VaultId == vault && row.Name.ToLower().Contains(query.Search.ToLower()))
            .OrderBy(row => row.Id).Select(row => new LibraryAsset(row.Id, row.VaultId, row.Name)), token);

    /// <inheritdoc />
    public Task<LibraryPage<LibraryMember>?> MembersAsync(Guid actor, Guid vault, LibraryQuery query, CancellationToken token) =>
        ReadAsync(actor, vault, true, query, context => context.Memberships.AsNoTracking()
            .Where(row => row.VaultId == vault).OrderBy(row => row.ActorId)
            .Select(row => new LibraryMember(row.ActorId, row.Role)), token);

    /// <inheritdoc />
    public Task<LibraryPage<LibraryRelationship>?> RelationshipsAsync(Guid actor, Guid vault, Guid? asset, LibraryQuery query, CancellationToken token) =>
        ReadAsync(actor, vault, false, query, context => context.Relationships.AsNoTracking()
            .Where(row => row.VaultId == vault && (asset == null || row.SourceAssetId == asset || row.TargetAssetId == asset))
            .OrderBy(row => row.Id).Select(row => new LibraryRelationship(row.Id, row.SourceAssetId, row.TargetAssetId, row.Status)), token);

    /// <inheritdoc />
    public Task<LibraryPage<LibraryReminder>?> RemindersAsync(Guid actor, Guid vault, Guid? asset, LibraryQuery query, CancellationToken token) =>
        ReadAsync(actor, vault, false, query, context => context.Reminders.AsNoTracking()
            .Where(row => row.VaultId == vault && (asset == null || row.AssetId == asset))
            .OrderBy(row => row.Id).Select(row => new LibraryReminder(row.Id, row.AssetId, row.DueAtUtcTicks, row.Status)), token);

    private async Task<LibraryPage<T>?> ReadAsync<T>(Guid actor, Guid? vault, bool administrators, LibraryQuery query,
        Func<HomeVaultDbContext, IQueryable<T>> project, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (actor == Guid.Empty || vault == Guid.Empty || !query.IsValid) return null;
        await using var context = database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, token);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (vault is not null && !await context.Memberships.AnyAsync(row => row.VaultId == vault && row.ActorId == actor &&
            (!administrators || row.Role == (int)VaultRole.Owner || row.Role == (int)VaultRole.Administrator), token)) return null;
        var rows = await project(context).Skip(query.Offset).Take(query.Limit + 1).ToListAsync(token);
        var more = rows.Count > query.Limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        await transaction.CommitAsync(token);
        return new LibraryPage<T>(rows, more);
    }
}
