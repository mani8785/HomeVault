using System.Security.Cryptography;
using System.Text;
using HomeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Infrastructure.Identity;

/// <summary>Identity-backed account operations with serialized SQLite mutations and safe outcomes.</summary>
/// <remarks>Scoped to one operation/request. Infrastructure exceptions must be mapped to constant diagnostics by the host.</remarks>
/// <param name="database">Scoped existing database context shared with the Identity store.</param>
/// <param name="users">Scoped framework manager using that context.</param>
/// <param name="clock">UTC clock for persisted credential expiration.</param>
public sealed class AccountOperations(HomeVaultDbContext database, UserManager<HomeVaultUser> users, TimeProvider clock)
{
    /// <summary>Checks that explicit schema upgrades have already completed.</summary>
    /// <returns>A task failing on incompatible schema.</returns>
    public Task ValidateStorageAsync() => SqliteDatabase.ValidateHistoryAsync(database, true, default);

    /// <summary>Issues an invitation or recovery credential for a verified recipient through the local operator only.</summary>
    /// <param name="login">Recipient login; never log it.</param>
    /// <param name="recovery">Whether to recover an existing enabled account, invalidating existing sessions immediately.</param>
    /// <returns>A secret for protected export, or null if the target is invalid.</returns>
    public async Task<AccountCredential?> IssueAsync(string login, bool recovery)
    {
        if (!ValidLogin(login)) return null;
        await using var transaction = await BeginAsync();
        var normalized = users.NormalizeName(login);
        var user = await users.FindByNameAsync(login);
        if (recovery ? user is null || !user.IsEnabled : user is not null) return null;
        if (!recovery)
        {
            var candidate = new HomeVaultUser { UserName = login, Email = login };
            foreach (var validator in users.UserValidators)
                if (!(await validator.ValidateAsync(users, candidate)).Succeeded) return null;
        }
        var purpose = recovery ? "recovery" : "invitation";
        await database.Set<AccountCredentialRow>().Where(row => row.Login == normalized && row.Purpose == purpose)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Consumed, true));
        string secret;
        if (recovery)
        {
            if (!(await users.UpdateSecurityStampAsync(user!)).Succeeded) return null;
            secret = await users.GeneratePasswordResetTokenAsync(user!);
        }
        else secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = clock.GetUtcNow().Add(recovery ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(24));
        database.Set<AccountCredentialRow>().Add(new AccountCredentialRow
        {
            Hash = Hash(secret),
            Login = normalized,
            Purpose = purpose,
            Expires = expires.ToUnixTimeSeconds()
        });
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return new AccountCredential { Purpose = purpose, Login = login, Secret = secret, Expires = expires };
    }

    /// <summary>Atomically consumes a credential and creates an account or resets its password; never signs in.</summary>
    /// <param name="login">Recipient identifier.</param>
    /// <param name="secret">Secret from the protected export.</param>
    /// <param name="password">Recipient-chosen password; bounded to 15–128 UTF-16 code units.</param>
    /// <param name="recovery">Selects recovery rather than invitation redemption.</param>
    /// <returns>False for invalid, expired, consumed or disabled credentials without disclosing which condition failed.</returns>
    public async Task<bool> RedeemAsync(string login, string secret, string password, bool recovery)
    {
        if (!ValidLogin(login) || secret is null || secret.Length is < 1 or > 4096 || password is null || password.Length is < 15 or > 128) return false;
        await using var transaction = await BeginAsync();
        var hash = Hash(secret);
        var normalized = users.NormalizeName(login);
        var purpose = recovery ? "recovery" : "invitation";
        var credential = await database.Set<AccountCredentialRow>().SingleOrDefaultAsync(row => row.Hash == hash);
        if (credential is null || credential.Consumed || credential.Login != normalized || credential.Purpose != purpose ||
            credential.Expires <= clock.GetUtcNow().ToUnixTimeSeconds()) return false;
        var user = await users.FindByNameAsync(login);
        IdentityResult result;
        if (recovery)
        {
            if (user is null || !user.IsEnabled) return false;
            result = await users.ResetPasswordAsync(user, secret, password);
            if (!result.Succeeded) return false;
            if (!(await users.SetLockoutEndDateAsync(user, null)).Succeeded || !(await users.ResetAccessFailedCountAsync(user)).Succeeded) return false;
        }
        else
        {
            if (user is not null) return false;
            user = new HomeVaultUser { UserName = login, Email = login, LockoutEnabled = true };
            result = await users.CreateAsync(user, password);
            if (!result.Succeeded) return false;
        }
        credential.Consumed = true;
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    /// <summary>Checks credentials and updates lockout atomically. All rejected account states share a null result.</summary>
    /// <param name="login">Submitted identifier.</param>
    /// <param name="password">Submitted secret, never logged.</param>
    /// <returns>The storage identity only on success; the host creates its cookie.</returns>
    public async Task<HomeVaultUser?> LoginAsync(string login, string password)
    {
        if (!ValidLogin(login) || password is null || password.Length is < 1 or > 128) return null;
        await using var transaction = await BeginAsync();
        var user = await users.FindByNameAsync(login);
        if (user is null || !user.IsEnabled || await users.IsLockedOutAsync(user))
        {
            // Similar password work for rejected identities; not a claim of identical timing.
            _ = users.PasswordHasher.HashPassword(new HomeVaultUser(), password);
            return null;
        }
        if (!await users.CheckPasswordAsync(user, password))
        {
            if (!(await users.AccessFailedAsync(user)).Succeeded) return null;
            await transaction.CommitAsync();
            return null;
        }
        if (!(await users.ResetAccessFailedCountAsync(user)).Succeeded) return null;
        await transaction.CommitAsync();
        return user;
    }

    /// <summary>Checks current enabled state and stamp without relying on stale cookie roles.</summary>
    /// <param name="id">Server-issued account identity.</param>
    /// <param name="stamp">Stamp protected within the authentication cookie.</param>
    /// <returns>True only for a current enabled account and exact stamp match.</returns>
    public async Task<bool> ValidateSessionAsync(Guid id, string? stamp)
    {
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id);
        return user is { IsEnabled: true } && stamp is not null && user.SecurityStamp == stamp;
    }

    /// <summary>Invalidates every session for an account; optionally disables it and revokes recovery.</summary>
    /// <param name="id">Trusted account identity.</param>
    /// <param name="disable">Operator-only disable action.</param>
    /// <returns>Whether an existing account was updated.</returns>
    public async Task<bool> RevokeAsync(Guid id, bool disable = false)
    {
        await using var transaction = await BeginAsync();
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return false;
        if (disable) user.IsEnabled = false;
        if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return false;
        await database.Set<AccountCredentialRow>().Where(row => row.Login == user.NormalizedUserName)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Consumed, true));
        await transaction.CommitAsync();
        return true;
    }

    /// <summary>Invalidates all sessions and outstanding credentials after offline restoration, preserving accounts and memberships.</summary>
    /// <returns>A task completing only after the atomic invalidation commits.</returns>
    public async Task InvalidateRestoredStateAsync()
    {
        await using var transaction = await BeginAsync();
        foreach (var user in await database.Users.ToListAsync())
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) throw new InvalidOperationException("Account invalidation failed.");
        await database.Set<AccountCredentialRow>().ExecuteUpdateAsync(update => update.SetProperty(row => row.Consumed, true));
        await transaction.CommitAsync();
    }

    private async Task<SqliteTransaction> BeginAsync()
    {
        await ValidateStorageAsync();
        await database.Database.OpenConnectionAsync();
        var transaction = ((SqliteConnection)database.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await database.Database.UseTransactionAsync(transaction);
        return transaction;
    }

    private static bool ValidLogin(string? login) => login is { Length: > 0 and <= 256 } && login.Contains('@') && !login.Any(char.IsWhiteSpace);
    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
