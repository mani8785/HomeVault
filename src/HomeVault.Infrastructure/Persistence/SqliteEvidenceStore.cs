using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Persists URL/Note Evidence with atomic access checks and explicit content disclosure.</summary>
/// <remarks>Content is plaintext. No method fetches or deletes external resources.</remarks>
public sealed class SqliteEvidenceStore : IEvidenceStore
{
    private readonly SqliteDatabase _database;
    /// <summary>Configures storage without opening or migrating it.</summary>
    /// <param name="database">Explicit SQLite configuration.</param>
    /// <exception cref="ArgumentNullException">Configuration is null.</exception>
    public SqliteEvidenceStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public Task<EvidenceOutcome> AddAsync(Guid assetId, Guid actorId, Guid id, string? label, EvidenceKind kind, string? content, CancellationToken cancellationToken) =>
        Mutate(assetId, actorId, id, label, kind, content, false, cancellationToken);
    /// <inheritdoc />
    public Task<EvidenceOutcome> RemoveAsync(Guid assetId, Guid actorId, Guid id, CancellationToken cancellationToken) =>
        Mutate(assetId, actorId, id, null, default, null, true, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EvidenceMetadata>?> ListAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (assetId == Guid.Empty || actorId == Guid.Empty) return null;
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var access = await Access(context, assetId, actorId).SingleOrDefaultAsync(cancellationToken);
        if (access is null) return null;
        ValidateAccess(access);
        // Content must not appear in the SQL projection for metadata inspection.
        var entries = await context.AssetEvidence.AsNoTracking().Where(row => row.AssetId == assetId)
            .Select(row => new EvidenceMetadata(row.Id, row.Label, (EvidenceKind)row.Kind)).ToArrayAsync(cancellationToken);
        if (entries.Any(entry => entry.Id == Guid.Empty || string.IsNullOrWhiteSpace(entry.Label) || !Enum.IsDefined(entry.Kind)))
            throw new InvalidOperationException("Invalid stored Evidence state.");
        return Array.AsReadOnly(entries);
    }

    /// <inheritdoc />
    public async Task<EvidenceContent?> ReadContentAsync(Guid assetId, Guid actorId, Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (assetId == Guid.Empty || actorId == Guid.Empty || id == Guid.Empty) return null;
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var access = await Access(context, assetId, actorId).SingleOrDefaultAsync(cancellationToken);
        if (access is null) return null;
        ValidateAccess(access);
        var row = await context.AssetEvidence.AsNoTracking().SingleOrDefaultAsync(row => row.AssetId == assetId && row.Id == id, cancellationToken);
        if (row is null) return null;
        var restored = Restore(access, [row]);
        return new EvidenceContent(restored.Evidence.Single().ReadContent());
    }

    private async Task<EvidenceOutcome> Mutate(Guid assetId, Guid actorId, Guid id, string? label, EvidenceKind kind, string? content, bool remove, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (assetId == Guid.Empty || actorId == Guid.Empty || id == Guid.Empty) throw new ArgumentException("Valid identities are required.");
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, token);
        await context.Database.OpenConnectionAsync(token);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await context.Database.UseTransactionAsync(transaction, token);
        var access = await Access(context, assetId, actorId).SingleOrDefaultAsync(token);
        if (access is null) return EvidenceOutcome.Unavailable;
        ValidateAccess(access);
        if (access.Role == (int)VaultRole.Viewer) return EvidenceOutcome.Forbidden;
        if (access.Status == (int)VaultStatus.Archived) return EvidenceOutcome.Archived;
        var rows = await context.AssetEvidence.AsNoTracking().Where(row => row.AssetId == assetId).ToArrayAsync(token);
        var asset = Restore(access, rows);
        var error = remove ? asset.RemoveEvidence(id) : asset.AddEvidence(id, label, kind, content);
        if (error != EvidenceError.None) return error switch
        {
            EvidenceError.EmptyIdentity => EvidenceOutcome.InvalidIdentity,
            EvidenceError.BlankLabel => EvidenceOutcome.BlankLabel,
            EvidenceError.InvalidKind => EvidenceOutcome.InvalidKind,
            EvidenceError.BlankContent => EvidenceOutcome.BlankContent,
            EvidenceError.InvalidUrl => EvidenceOutcome.InvalidUrl,
            EvidenceError.DuplicateIdentity => EvidenceOutcome.DuplicateIdentity,
            EvidenceError.NotFound => EvidenceOutcome.Unavailable,
            _ => throw new InvalidOperationException("Unexpected Evidence outcome.")
        };
        if (remove)
        {
            if (await context.AssetEvidence.Where(row => row.AssetId == assetId && row.Id == id).ExecuteDeleteAsync(token) != 1)
                throw new InvalidOperationException("Invalid stored Evidence state.");
        }
        else
        {
            var added = asset.Evidence.Single(entry => entry.Id == id);
            context.AssetEvidence.Add(new EvidenceRow { AssetId = assetId, Id = id, Label = added.Label, Kind = (int)added.Kind, Content = added.ReadContent() });
            await context.SaveChangesAsync(token);
        }
        await transaction.CommitAsync(token);
        return EvidenceOutcome.Succeeded;
    }

    private static Asset Restore(AccessState access, IEnumerable<EvidenceRow> rows) =>
        Asset.RestoreEvidence(access.Id, access.VaultId, access.Name, rows.Select(row => (row.Id, row.Label, (EvidenceKind)row.Kind, row.Content)));

    private static IQueryable<AccessState> Access(HomeVaultDbContext context, Guid assetId, Guid actorId) =>
        from asset in context.Assets.AsNoTracking()
        join vault in context.Vaults on asset.VaultId equals vault.Id
        join member in context.Memberships on vault.Id equals member.VaultId
        where asset.Id == assetId && member.ActorId == actorId
        select new AccessState { Id = asset.Id, VaultId = vault.Id, Name = asset.Name, Role = member.Role, Status = vault.Status };

    private static void ValidateAccess(AccessState access)
    {
        if (!Enum.IsDefined((VaultRole)access.Role) || !Enum.IsDefined((VaultStatus)access.Status))
            throw new InvalidOperationException("Invalid stored access state.");
    }
    private sealed class AccessState
    {
        public Guid Id { get; init; }
        public Guid VaultId { get; init; }
        public string Name { get; init; } = "";
        public int Role { get; init; }
        public int Status { get; init; }
    }
}
