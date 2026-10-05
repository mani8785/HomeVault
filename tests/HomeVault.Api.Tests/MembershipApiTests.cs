using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace HomeVault.Api.Tests;

public sealed partial class RecordsApiTests
{
    private const string Members = "/api/v1/vaults/{vaultId}/members";
    private const string MemberRole = "/api/v1/vaults/{vaultId}/members/{targetActorId}/role";
    private const string Member = "/api/v1/vaults/{vaultId}/members/{targetActorId}";

    [Test]
    public async Task MembershipJourneyChangesAccessImmediatelyAndSurvivesRestart()
    {
        var vault = await CreateVault();
        var asset = await CreateAsset(vault);
        var route = $"{Vaults}/{vault:D}/members";
        await Contract(await _other.Send("GET", $"/api/v1/assets/{asset:D}"), Inspection, "get", 404);
        await Contract(await _owner.Send("POST", route, new { targetActorId = _otherId, role = "editor" }), Members, "post", 204);
        await Contract(await _other.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Permitted" }), Registration, "post", 201);
        await _app.DisposeAsync();
        await Start();
        _owner.Replace(_app.GetTestClient());
        _other.Replace(_app.GetTestClient());
        await Contract(await _other.Send("GET", $"/api/v1/assets/{asset:D}"), Inspection, "get", 200);
        await Contract(await _owner.Send("PUT", $"{route}/{_otherId:D}/role", new { role = "viewer" }), MemberRole, "put", 204);
        await Contract(await _other.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Denied" }), Registration, "post", 403);
        await Contract(await _owner.Send("DELETE", $"{route}/{_otherId:D}"), Member, "delete", 204);
        await Contract(await _other.Send("GET", $"/api/v1/assets/{asset:D}"), Inspection, "get", 404);
        Assert.That(await AssetCount(), Is.EqualTo(2));
    }

    [TestCase("owner", 204)]
    [TestCase("administrator", 204)]
    [TestCase("editor", 403)]
    [TestCase("viewer", 403)]
    public async Task MembershipHttpRespectsRequesterRoleAndProtectsPrivilegedTargets(string role, int expected)
    {
        var vault = await CreateVault();
        var route = $"{Vaults}/{vault:D}/members";
        await Contract(await _owner.Send("POST", route, new { targetActorId = _otherId, role }), Members, "post", 204);
        using var third = new Browser(_app.GetTestClient());
        var thirdId = await EnrollAndLogin(third, "third@example.invalid");
        await Contract(await _other.Send("POST", route, new { targetActorId = thirdId, role = "viewer" }), Members, "post", expected);
        if (expected != 204)
            await Contract(await _owner.Send("POST", route, new { targetActorId = thirdId, role = "viewer" }), Members, "post", 204);
        await Contract(await _other.Send("PUT", $"{route}/{thirdId:D}/role", new { role = "editor" }), MemberRole, "put", expected);
        await Contract(await _other.Send("DELETE", $"{route}/{thirdId:D}"), Member, "delete", expected);
        if (role == "administrator")
        {
            await Contract(await _other.Send("PUT", $"{route}/{_ownerId:D}/role", new { role = "viewer" }), MemberRole, "put", 403);
            await Contract(await _other.Send("PUT", $"{route}/{_otherId:D}/role", new { role = "owner" }), MemberRole, "put", 403);
            await Contract(await _other.Send("DELETE", $"{route}/{_ownerId:D}"), Member, "delete", 403);
            await Contract(await _other.Send("POST", route, new { targetActorId = thirdId, role = "owner" }), Members, "post", 403);
        }
    }

    [Test]
    public async Task MembershipErrorsCoverLastOwnerDisabledTargetsAndArchivedWrites()
    {
        var vault = await CreateVault();
        var route = $"{Vaults}/{vault:D}/members";
        var last = await Contract(await _owner.Send("DELETE", $"{route}/{_ownerId:D}"), Member, "delete", 409);
        Assert.That(last.GetProperty("code").GetString(), Is.EqualTo("last_owner"));
        await Contract(await _owner.Send("PUT", $"{route}/{_ownerId:D}/role", new { role = "viewer" }), MemberRole, "put", 409);
        await Contract(await _owner.Send("POST", route, new { targetActorId = _otherId, role = "viewer" }), Members, "post", 204);
        var duplicate = await Contract(await _owner.Send("POST", route, new { targetActorId = _otherId, role = "viewer" }), Members, "post", 409);
        Assert.That(duplicate.GetProperty("code").GetString(), Is.EqualTo("duplicate_member"));
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().RevokeAsync(_otherId, disable: true);
        await Contract(await _owner.Send("PUT", $"{route}/{_otherId:D}/role", new { role = "editor" }), MemberRole, "put", 204);
        await Contract(await _owner.Send("DELETE", $"{route}/{_otherId:D}"), Member, "delete", 204);
        var disabled = await Contract(await _owner.Send("POST", route, new { targetActorId = _otherId, role = "viewer" }), Members, "post", 404);
        var absent = await Contract(await _owner.Send("POST", route, new { targetActorId = Guid.NewGuid(), role = "viewer" }), Members, "post", 404);
        Assert.That(disabled.GetRawText(), Is.EqualTo(absent.GetRawText()));
        await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/archive"), Archival, "post", 204);
        await Contract(await _owner.Send("POST", route, new { targetActorId = _otherId, role = "viewer" }), Members, "post", 409);
        await Contract(await _owner.Send("PUT", $"{route}/{_ownerId:D}/role", new { role = "owner" }), MemberRole, "put", 409);
        await Contract(await _owner.Send("DELETE", $"{route}/{_otherId:D}"), Member, "delete", 409);
    }

    [TestCase("POST")]
    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task MembershipRoutesRequireRealIdentityAndAntiforgeryAndRejectSpoofing(string method)
    {
        var vault = await CreateVault();
        var route = $"{Vaults}/{vault:D}/members";
        var path = method == "POST" ? route : $"{route}/{_otherId:D}" + (method == "PUT" ? "/role" : "");
        var contract = method == "POST" ? Members : method == "PUT" ? MemberRole : Member;
        object? body = method == "POST" ? new { targetActorId = _otherId, role = "viewer" } : method == "PUT" ? new { role = "viewer" } : null;
        using var anonymous = new Browser(_app.GetTestClient());
        await Contract(await anonymous.Send(method, path, body), contract, method.ToLowerInvariant(), 401);
        await Contract(await _owner.Send(method, path, body, csrf: false), contract, method.ToLowerInvariant(), 400);
        await Contract(await _owner.Send(method, path, new { actorId = _otherId, role = "viewer", targetActorId = _otherId }), contract, method.ToLowerInvariant(), 400);
        var denied = await Contract(await _other.Send(method, path + $"?actorId={_ownerId:D}", body, actorHeader: _ownerId), contract, method.ToLowerInvariant(), 404);
        var missing = await Contract(await _other.Send(method, path.Replace(vault.ToString("D"), Guid.NewGuid().ToString("D")), body), contract, method.ToLowerInvariant(), 404);
        Assert.That(denied.GetRawText(), Is.EqualTo(missing.GetRawText()));
        using (var scope = _app.Services.CreateScope())
            Assert.That(await scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>().Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS Value FROM Memberships WHERE VaultId = {vault}").SingleAsync(), Is.EqualTo(1));
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().RevokeAsync(_ownerId);
        await Contract(await _owner.Send(method, path, body), contract, method.ToLowerInvariant(), 401);
    }

    [Test]
    public async Task MembershipInputAndStorageFailuresAreSafe()
    {
        var vault = await CreateVault();
        var route = $"{Vaults}/{vault:D}/members";
        await Contract(await _owner.Send("POST", route, new { targetActorId = "invalid", role = "viewer" }), Members, "post", 400);
        await Contract(await _owner.Send("POST", route, new { targetActorId = _otherId, role = "Owner" }), Members, "post", 400);
        await Contract(await _owner.Send("PUT", $"{route}/{_ownerId:D}/role", new { role = 0 }), MemberRole, "put", 400);
        await Sql($"CREATE TRIGGER FailMembership BEFORE INSERT ON Memberships BEGIN SELECT RAISE(ABORT, 'private membership failure'); END;");
        var failure = await Contract(await _owner.Send("POST", route, new { targetActorId = _otherId, role = "viewer" }), Members, "post", 500);
        Assert.That(failure.GetRawText(), Does.Not.Contain("private").And.Not.Contain(_path));
    }

    [TestCase("POST", false)]
    [TestCase("PUT", false)]
    [TestCase("DELETE", false)]
    [TestCase("POST", true)]
    [TestCase("PUT", true)]
    [TestCase("DELETE", true)]
    public async Task MembershipChecksCommittedChangesAfterAuthentication(string method, bool archive)
    {
        var vault = await CreateVault();
        var route = $"{Vaults}/{vault:D}/members";
        if (method != "POST") await Sql($"INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ({vault}, {_otherId}, {0})");
        var path = method == "POST" ? route : $"{route}/{_otherId:D}" + (method == "PUT" ? "/role" : "");
        var contract = method == "POST" ? Members : method == "PUT" ? MemberRole : Member;
        object? body = method == "POST" ? new { targetActorId = _otherId, role = "viewer" } : method == "PUT" ? new { role = "viewer" } : null;
        _gate.Enabled = true;
        var pending = _owner.Send(method, path, body);
        await _gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            if (archive) await Sql($"UPDATE Vaults SET Status = {1} WHERE Id = {vault}");
            else
            {
                if (method == "POST") await Sql($"INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ({vault}, {Guid.NewGuid()}, {0})");
                await Sql($"UPDATE Memberships SET Role = {2} WHERE VaultId = {vault} AND ActorId = {_ownerId}");
            }
        }
        finally { _gate.Release.TrySetResult(); }
        await Contract(await pending, contract, method.ToLowerInvariant(), archive ? 409 : 403);
        using var scope = _app.Services.CreateScope();
        var roles = await scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>().Database.SqlQuery<int>(
            $"SELECT Role AS Value FROM Memberships WHERE VaultId = {vault} AND ActorId = {_otherId}").ToListAsync();
        Assert.That(roles, Is.EqualTo(method == "POST" ? Array.Empty<int>() : new[] { 0 }));
    }

    private sealed class GatedMembershipStore(IVaultMembershipStore inner, RegistrationGate gate) : IVaultMembershipStore
    {
        private async Task<MembershipOutcome> Invoke(Func<Task<MembershipOutcome>> mutation, CancellationToken token)
        {
            if (gate.Enabled)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            }
            return await mutation();
        }
        public Task<MembershipOutcome> AddAsync(Guid vaultId, Guid actorId, Guid targetActorId, VaultRole role, CancellationToken cancellationToken) =>
            Invoke(() => inner.AddAsync(vaultId, actorId, targetActorId, role, cancellationToken), cancellationToken);
        public Task<MembershipOutcome> ChangeRoleAsync(Guid vaultId, Guid actorId, Guid targetActorId, VaultRole role, CancellationToken cancellationToken) =>
            Invoke(() => inner.ChangeRoleAsync(vaultId, actorId, targetActorId, role, cancellationToken), cancellationToken);
        public Task<MembershipOutcome> RemoveAsync(Guid vaultId, Guid actorId, Guid targetActorId, CancellationToken cancellationToken) =>
            Invoke(() => inner.RemoveAsync(vaultId, actorId, targetActorId, cancellationToken), cancellationToken);
    }
}
