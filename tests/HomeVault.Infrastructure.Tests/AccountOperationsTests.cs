using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[TestFixture]
public sealed class AccountOperationsTests
{
    private string _directory = null!;
    private ServiceProvider _services = null!;
    private TestClock _clock = null!;
    private const string Login = "fictional@example.invalid";
    private const string Password = "a fictional long passphrase";

    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVaultTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "accounts.db");
        await new SqliteDatabase(path).MigrateAsync();
        _clock = new TestClock();
        var services = new ServiceCollection().AddHomeVaultAccounts(path, new EphemeralDataProtectionProvider());
        services.AddSingleton<TimeProvider>(_clock);
        _services = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() { _services.Dispose(); Directory.Delete(_directory, true); }

    [Test]
    public async Task InvitationIsBoundHashedAndConsumedAtomicallyUnderConcurrency()
    {
        var credential = await Run(accounts => accounts.IssueAsync(Login, false));
        Assert.That(credential, Is.Not.Null);
        Assert.That(credential!.ToString(), Does.Not.Contain(credential.Secret));
        Assert.That(await Run(accounts => accounts.RedeemAsync("different@example.invalid", credential.Secret, Password, false)), Is.False);
        Assert.That(await Run(accounts => accounts.RedeemAsync(Login, credential.Secret, "short", false)), Is.False);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
            Run(accounts => accounts.RedeemAsync(Login, credential.Secret, Password, false)))));
        Assert.That(attempts.Count(success => success), Is.EqualTo(1));
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>();
        Assert.That(await db.Users.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.Memberships.CountAsync(), Is.Zero);
        var stored = await db.Set<AccountCredentialRow>().SingleAsync();
        Assert.That(stored.Hash, Is.Not.EqualTo(credential.Secret));
        Assert.That(stored.Consumed, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReissueRevokesPriorCredentialAndExpiryIsEnforced(bool recovery)
    {
        if (recovery) await Enroll();
        var old = (await Run(accounts => accounts.IssueAsync(Login, recovery)))!;
        var current = (await Run(accounts => accounts.IssueAsync(Login, recovery)))!;
        Assert.That(await Run(accounts => accounts.RedeemAsync(Login, old.Secret, Password, recovery)), Is.False);
        _clock.Now = current.Expires;
        Assert.That(await Run(accounts => accounts.RedeemAsync(Login, current.Secret, Password, recovery)), Is.False);
    }

    [Test]
    public async Task RecoveryRevokesSessionsAndCannotBeReplayedOrRaceTwoResets()
    {
        await Enroll();
        var user = (await Run(accounts => accounts.LoginAsync(Login, Password)))!;
        var recovery = (await Run(accounts => accounts.IssueAsync(Login, true)))!;
        Assert.That(await Run(accounts => accounts.ValidateSessionAsync(user.Id, user.SecurityStamp)), Is.False);
        const string changed = "changed fictional passphrase";
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            Run(accounts => accounts.RedeemAsync(Login, recovery.Secret, changed, true)))));
        Assert.That(attempts.Count(success => success), Is.EqualTo(1));
        Assert.That(await Run(accounts => accounts.LoginAsync(Login, Password)), Is.Null);
        Assert.That(await Run(accounts => accounts.LoginAsync(Login, changed)), Is.Not.Null);
        Assert.That(await Run(accounts => accounts.RedeemAsync(Login, recovery.Secret, Password, true)), Is.False);
    }

    [Test]
    public async Task FiveFailuresLockAccountAndRecoveryClearsLockoutWithoutEnablingDisabledUsers()
    {
        await Enroll();
        for (var count = 0; count < 5; count++)
            Assert.That(await Run(accounts => accounts.LoginAsync(Login, "wrong fictional password")), Is.Null);
        Assert.That(await Run(accounts => accounts.LoginAsync(Login, Password)), Is.Null);
        using (var scope = _services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>().Users.SingleAsync();
            Assert.That(user.LockoutEnd, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(14)));
        }
        var recovery = (await Run(accounts => accounts.IssueAsync(Login, true)))!;
        Assert.That(await Run(accounts => accounts.RedeemAsync(Login, recovery.Secret, Password, true)), Is.True);
        var enabled = (await Run(accounts => accounts.LoginAsync(Login, Password)))!;
        Assert.That(await Run(accounts => accounts.RevokeAsync(enabled.Id, disable: true)), Is.True);
        Assert.That(await Run(accounts => accounts.LoginAsync(Login, Password)), Is.Null);
        Assert.That(await Run(accounts => accounts.IssueAsync(Login, true)), Is.Null);
    }

    [Test]
    public async Task RestoreInvalidationPreservesPasswordAndRejectsAllOutstandingCredentials()
    {
        await Enroll();
        var recovery = (await Run(accounts => accounts.IssueAsync(Login, true)))!;
        var user = (await Run(accounts => accounts.LoginAsync(Login, Password)))!;
        var invitation = (await Run(accounts => accounts.IssueAsync("other@example.invalid", false)))!;
        using (var scope = _services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().InvalidateRestoredStateAsync();
        Assert.That(await Run(accounts => accounts.ValidateSessionAsync(user.Id, user.SecurityStamp)), Is.False);
        Assert.That(await Run(accounts => accounts.RedeemAsync(Login, recovery.Secret, Password, true)), Is.False);
        Assert.That(await Run(accounts => accounts.RedeemAsync(invitation.Login, invitation.Secret, Password, false)), Is.False);
        Assert.That(await Run(accounts => accounts.LoginAsync(Login, Password)), Is.Not.Null);
    }

    private async Task Enroll()
    {
        var credential = (await Run(accounts => accounts.IssueAsync(Login, false)))!;
        Assert.That(await Run(accounts => accounts.RedeemAsync(Login, credential.Secret, Password, false)), Is.True);
    }

    private async Task<T> Run<T>(Func<AccountOperations, Task<T>> operation)
    {
        using var scope = _services.CreateScope();
        return await operation(scope.ServiceProvider.GetRequiredService<AccountOperations>());
    }

    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
