using HomeVault.Application.Relationships;
using HomeVault.Domain.Relationships;
using HomeVault.Domain.Vaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Enforces actual same-Vault endpoints and current access inside consistent SQLite transactions.</summary>
public sealed class SqliteRelationshipStore : IRelationshipStore
{
    private readonly SqliteDatabase _database;
    /// <summary>Configures the existing local database without opening or migrating it.</summary>
    /// <param name="database">Explicit database configuration.</param>
    /// <exception cref="ArgumentNullException">Configuration is null.</exception>
    public SqliteRelationshipStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }
    /// <inheritdoc />
    public Task<RelationshipOutcome> CreateAsync(Guid vaultId, Guid actorId, Guid id, Guid sourceAssetId, Guid targetAssetId, RelationshipKind kind, CancellationToken cancellationToken) =>
        Mutate(vaultId, actorId, id, sourceAssetId, targetAssetId, kind, false, cancellationToken);
    /// <inheritdoc />
    public Task<RelationshipOutcome> RemoveAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken cancellationToken) =>
        Mutate(vaultId, actorId, id, Guid.Empty, Guid.Empty, default, true, cancellationToken);

    /// <inheritdoc />
    public async Task<RelationshipMetadata?> FindAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (vaultId == Guid.Empty || actorId == Guid.Empty || id == Guid.Empty) return null;
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var access = await Access(context, vaultId, actorId).SingleOrDefaultAsync(cancellationToken);
        if (access is null) return null;
        ValidateAccess(access);
        var row = await context.Relationships.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.VaultId == vaultId, cancellationToken);
        if (row is null) return null;
        var root = await Restore(context, row, cancellationToken);
        return new RelationshipMetadata(root.Id, root.VaultId, root.SourceAssetId, root.TargetAssetId, root.Kind, root.Status);
    }

    private async Task<RelationshipOutcome> Mutate(Guid vaultId, Guid actorId, Guid id, Guid source, Guid target, RelationshipKind kind, bool remove, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (vaultId == Guid.Empty || actorId == Guid.Empty || id == Guid.Empty || (!remove && (source == Guid.Empty || target == Guid.Empty)))
            throw new ArgumentException("Valid identities are required.");
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, token);
        await context.Database.OpenConnectionAsync(token);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await context.Database.UseTransactionAsync(transaction, token);
        var access = await Access(context, vaultId, actorId).SingleOrDefaultAsync(token);
        if (access is null) return RelationshipOutcome.Unavailable;
        ValidateAccess(access);
        if (access.Role == (int)VaultRole.Viewer) return RelationshipOutcome.Forbidden;
        if (access.Status == (int)VaultStatus.Archived) return RelationshipOutcome.Archived;

        if (remove)
        {
            var row = await context.Relationships.SingleOrDefaultAsync(row => row.Id == id && row.VaultId == vaultId, token);
            if (row is null) return RelationshipOutcome.Unavailable;
            var root = await Restore(context, row, token);
            root.Remove();
            row.Status = (int)root.Status;
        }
        else
        {
            if (!await OwnsEndpoints(context, vaultId, source, target, token)) return RelationshipOutcome.Unavailable;
            var created = Relationship.Create(id, vaultId, source, target, kind);
            if (created.Relationship is not { } root) return created.Error switch
            {
                RelationshipCreationError.InvalidKind => RelationshipOutcome.InvalidKind,
                RelationshipCreationError.SelfReference => RelationshipOutcome.SelfReference,
                _ => throw new InvalidOperationException("Unexpected Relationship creation outcome.")
            };
            var existing = await context.Relationships.AsNoTracking().Where(row => row.Id == id).Select(row => (Guid?)row.VaultId).SingleOrDefaultAsync(token);
            if (existing.HasValue) return existing.Value == vaultId ? RelationshipOutcome.IdentityConflict : RelationshipOutcome.Unavailable;
            if (await context.Relationships.AnyAsync(row => row.VaultId == vaultId && row.SourceAssetId == source && row.TargetAssetId == target && row.Kind == (int)kind && row.Status == (int)RelationshipStatus.Active, token))
                return RelationshipOutcome.DuplicateRelationship;
            context.Relationships.Add(new RelationshipRow
            {
                Id = root.Id,
                VaultId = root.VaultId,
                SourceAssetId = root.SourceAssetId,
                TargetAssetId = root.TargetAssetId,
                Kind = (int)root.Kind,
                Status = (int)root.Status
            });
        }
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return RelationshipOutcome.Succeeded;
    }

    private static async Task<Relationship> Restore(HomeVaultDbContext context, RelationshipRow row, CancellationToken token)
    {
        var root = Relationship.Restore(row.Id, row.VaultId, row.SourceAssetId, row.TargetAssetId, (RelationshipKind)row.Kind, (RelationshipStatus)row.Status);
        if (!await OwnsEndpoints(context, root.VaultId, root.SourceAssetId, root.TargetAssetId, token))
            throw new InvalidOperationException("Invalid stored Relationship ownership.");
        return root;
    }
    private static async Task<bool> OwnsEndpoints(HomeVaultDbContext context, Guid vaultId, Guid source, Guid target, CancellationToken token) =>
        await context.Assets.AnyAsync(asset => asset.Id == source && asset.VaultId == vaultId, token) &&
        await context.Assets.AnyAsync(asset => asset.Id == target && asset.VaultId == vaultId, token);

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
    private sealed class AccessState
    {
        public int Role { get; init; }
        public int Status { get; init; }
    }
}
