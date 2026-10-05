using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Persistence;

/// <summary>Enforces current membership permissions and mutations within one SQLite write transaction.</summary>
public sealed class SqliteVaultMembershipStore : IVaultMembershipStore
{
    private readonly SqliteDatabase _database;

    /// <summary>Configures the existing database without opening or migrating it.</summary>
    /// <param name="database">Explicit local database configuration.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    public SqliteVaultMembershipStore(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public Task<MembershipOutcome> AddAsync(Guid vaultId, Guid actorId, Guid targetActorId, VaultRole role, CancellationToken cancellationToken) =>
        Mutate(vaultId, actorId, targetActorId, role, Operation.Add, cancellationToken);

    /// <inheritdoc />
    public Task<MembershipOutcome> ChangeRoleAsync(Guid vaultId, Guid actorId, Guid targetActorId, VaultRole role, CancellationToken cancellationToken) =>
        Mutate(vaultId, actorId, targetActorId, role, Operation.ChangeRole, cancellationToken);

    /// <inheritdoc />
    public Task<MembershipOutcome> RemoveAsync(Guid vaultId, Guid actorId, Guid targetActorId, CancellationToken cancellationToken) =>
        Mutate(vaultId, actorId, targetActorId, VaultRole.Viewer, Operation.Remove, cancellationToken);

    private enum Operation { Add, ChangeRole, Remove }

    private async Task<MembershipOutcome> Mutate(Guid vaultId, Guid actorId, Guid targetActorId, VaultRole role, Operation operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (vaultId == Guid.Empty || actorId == Guid.Empty || targetActorId == Guid.Empty || !Enum.IsDefined(role))
            throw new ArgumentException("Valid identities and role are required.");
        await using var context = _database.CreateContext();
        await SqliteDatabase.ValidateHistoryAsync(context, true, token);
        await context.Database.OpenConnectionAsync(token);
        await using var transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await context.Database.UseTransactionAsync(transaction, token);
        var access = await (from vault in context.Vaults
                            join member in context.Memberships on vault.Id equals member.VaultId
                            where vault.Id == vaultId && member.ActorId == actorId
                            select new { member.Role }).SingleOrDefaultAsync(token);
        if (access is null) return MembershipOutcome.Unavailable;
        if (access.Role is not ((int)VaultRole.Owner or (int)VaultRole.Administrator)) return MembershipOutcome.Forbidden;
        var target = await context.Memberships.SingleOrDefaultAsync(member => member.VaultId == vaultId && member.ActorId == targetActorId, token);
        if (access.Role == (int)VaultRole.Administrator &&
            ((operation != Operation.Remove && !IsOrdinary(role)) ||
             (target is not null && !IsOrdinary((VaultRole)target.Role)))) return MembershipOutcome.Forbidden;
        var row = await context.Vaults.SingleAsync(vault => vault.Id == vaultId, token);
        var members = await context.Memberships.Where(member => member.VaultId == vaultId).ToListAsync(token);
        var restored = Vault.Restore(row.Id, row.Name, (VaultType)row.Type, (VaultStatus)row.Status,
            members.Select(member => new KeyValuePair<Guid, VaultRole>(member.ActorId, (VaultRole)member.Role)));
        if (restored.Status == VaultStatus.Archived) return MembershipOutcome.Archived;
        // Only additions require a live target account; disabled members must remain removable.
        if (operation == Operation.Add && !await context.Users.AnyAsync(user => user.Id == targetActorId && user.IsEnabled, token))
            return MembershipOutcome.Unavailable;
        var error = operation switch
        {
            Operation.Add => restored.AddMember(targetActorId, role),
            Operation.ChangeRole => restored.ChangeMemberRole(targetActorId, role),
            Operation.Remove => restored.RemoveMember(targetActorId),
            _ => throw new InvalidOperationException("Unexpected membership operation.")
        };
        if (error != VaultMembershipError.None) return error switch
        {
            VaultMembershipError.DuplicateMember => MembershipOutcome.DuplicateMember,
            VaultMembershipError.MemberNotFound => MembershipOutcome.Unavailable,
            VaultMembershipError.LastOwner => MembershipOutcome.LastOwner,
            VaultMembershipError.Archived => MembershipOutcome.Archived,
            _ => throw new InvalidOperationException("Unexpected membership outcome.")
        };
        if (operation == Operation.Add)
            context.Memberships.Add(new MembershipRow { VaultId = vaultId, ActorId = targetActorId, Role = (int)role });
        else if (operation == Operation.Remove) context.Memberships.Remove(target!);
        else target!.Role = (int)role;
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return MembershipOutcome.Succeeded;
    }

    private static bool IsOrdinary(VaultRole role) => role is VaultRole.Editor or VaultRole.Viewer;
}
