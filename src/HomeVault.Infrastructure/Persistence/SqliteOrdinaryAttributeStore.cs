using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using HomeVault.Domain.Vaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Stores ordinary attributes with transactionally consistent current membership and lifecycle checks.</summary>
public sealed class SqliteOrdinaryAttributeStore : IOrdinaryAttributeStore
{
    private readonly SqliteDatabase _database;

    /// <summary>Configures storage without opening or migrating it.</summary>
    /// <param name="database">Local SQLite configuration.</param>
    /// <exception cref="ArgumentNullException">Configuration is null.</exception>
    public SqliteOrdinaryAttributeStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public Task<AttributeOutcome> AddAsync(Guid assetId, Guid actorId, string? name, string? value, AttributeSensitivity sensitivity, CancellationToken cancellationToken) =>
        Mutate(assetId, actorId, name, value, sensitivity, Operation.Add, cancellationToken);
    /// <inheritdoc />
    public Task<AttributeOutcome> ChangeAsync(Guid assetId, Guid actorId, string? name, string? value, CancellationToken cancellationToken) =>
        Mutate(assetId, actorId, name, value, AttributeSensitivity.Ordinary, Operation.Change, cancellationToken);
    /// <inheritdoc />
    public Task<AttributeOutcome> RemoveAsync(Guid assetId, Guid actorId, string? name, CancellationToken cancellationToken) =>
        Mutate(assetId, actorId, name, null, AttributeSensitivity.Ordinary, Operation.Remove, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssetAttribute>?> ListAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken)
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
        ValidateAccessState(access);
        var asset = await Restore(context, access, cancellationToken);
        return asset.Attributes;
    }

    private enum Operation { Add, Change, Remove }

    private async Task<AttributeOutcome> Mutate(Guid assetId, Guid actorId, string? name, string? value,
        AttributeSensitivity sensitivity, Operation operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (assetId == Guid.Empty || actorId == Guid.Empty) throw new ArgumentException("Valid identities are required.");
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, token);
        await context.Database.OpenConnectionAsync(token);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await context.Database.UseTransactionAsync(transaction, token);
        var access = await Access(context, assetId, actorId).SingleOrDefaultAsync(token);
        if (access is null) return AttributeOutcome.Unavailable;
        ValidateAccessState(access);
        if (access.Role == (int)VaultRole.Viewer) return AttributeOutcome.Forbidden;
        if (access.Status == (int)VaultStatus.Archived) return AttributeOutcome.Archived;
        var asset = await Restore(context, access, token);
        // Unsupported classification is converted to an invalid enum before Domain validation.
        // This preserves name/value precedence without ever constructing a Sensitive entry.
        var error = operation switch
        {
            Operation.Add => asset.AddAttribute(name, value, sensitivity == AttributeSensitivity.Ordinary ? sensitivity : (AttributeSensitivity)(-1)),
            Operation.Change => asset.ChangeAttribute(name, value),
            Operation.Remove => asset.RemoveAttribute(name),
            _ => throw new InvalidOperationException("Unexpected attribute operation.")
        };
        if (error != AssetAttributeError.None) return error switch
        {
            AssetAttributeError.BlankName => AttributeOutcome.InvalidName,
            AssetAttributeError.BlankValue => AttributeOutcome.InvalidValue,
            AssetAttributeError.InvalidSensitivity => AttributeOutcome.UnsupportedSensitivity,
            AssetAttributeError.DuplicateName => AttributeOutcome.DuplicateName,
            AssetAttributeError.NotFound => AttributeOutcome.Unavailable,
            _ => throw new InvalidOperationException("Unexpected attribute outcome.")
        };
        var key = name!.Trim();
        if (operation == Operation.Add)
        {
            var added = asset.Attributes.Single(entry => StringComparer.OrdinalIgnoreCase.Equals(entry.Name, key));
            context.AssetAttributes.Add(new AssetAttributeRow { AssetId = assetId, Name = added.Name, Value = added.Value!, Sensitivity = 0 });
            await context.SaveChangesAsync(token);
        }
        else
        {
            // Match in .NET, then constrain the one-row write by exact stored name and classification.
            var names = await context.AssetAttributes.Where(row => row.AssetId == assetId).Select(row => row.Name).ToListAsync(token);
            var storedName = names.Single(stored => StringComparer.OrdinalIgnoreCase.Equals(stored, key));
            var target = context.AssetAttributes.Where(row => row.AssetId == assetId && row.Name == storedName && row.Sensitivity == 0);
            var count = operation == Operation.Remove
                ? await target.ExecuteDeleteAsync(token)
                : await target.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, value!), token);
            if (count != 1) throw new InvalidOperationException("Invalid stored attribute state.");
        }
        await transaction.CommitAsync(token);
        return AttributeOutcome.Succeeded;
    }

    private static async Task<Asset> Restore(HomeVaultDbContext context, AccessState access, CancellationToken token)
    {
        if (await context.AssetAttributes.AnyAsync(row => row.AssetId == access.Id && row.Sensitivity != 0, token))
            throw new InvalidOperationException("Unsupported stored attribute classification.");
        var rows = await context.AssetAttributes.AsNoTracking().Where(row => row.AssetId == access.Id && row.Sensitivity == 0).ToListAsync(token);
        return Asset.RestoreOrdinaryAttributes(access.Id, access.VaultId, access.Name,
            rows.Select(row => new KeyValuePair<string, string>(row.Name, row.Value)));
    }

    private static IQueryable<AccessState> Access(HomeVaultDbContext context, Guid assetId, Guid actorId) =>
        from asset in context.Assets.AsNoTracking()
        join vault in context.Vaults on asset.VaultId equals vault.Id
        join member in context.Memberships on vault.Id equals member.VaultId
        where asset.Id == assetId && member.ActorId == actorId
        select new AccessState { Id = asset.Id, VaultId = vault.Id, Name = asset.Name, Role = member.Role, Status = vault.Status };

    private static void ValidateAccessState(AccessState access)
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
