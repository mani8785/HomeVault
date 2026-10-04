using HomeVault.Application.Reminders;
using Microsoft.AspNetCore.TestHost;
using NUnit.Framework;

namespace HomeVault.Api.Tests;

public sealed partial class RecordsApiTests
{
    private const string Reminders = "/api/v1/vaults/{vaultId}/reminders";
    private const string ReminderEntry = Reminders + "/{reminderId}";
    private const string ReminderAction = ReminderEntry + "/action";
    private const string Due = "2030-01-02T03:04:05.1234567+02:30";

    [Test]
    public async Task ReminderJourneyPreservesExactUtcAndPrivateActionAcrossRestart()
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault); var id = Guid.NewGuid();
        var path = $"{Vaults}/{vault:D}/reminders"; var entry = $"{path}/{id:D}";
        var response = await _owner.Send("POST", path, new { id, assetId = asset, action = " fictional private action ", dueAt = Due });
        var created = await Contract(response, Reminders, "post", 201);
        Assert.That(response.Headers.Location!.OriginalString, Is.EqualTo(entry));
        Assert.That(created.GetProperty("dueAt").GetString(), Is.EqualTo("2030-01-02T00:34:05.1234567Z"));
        Assert.That(created.GetRawText(), Does.Not.Contain("private action"));
        await Contract(await _owner.Send("PUT", entry, new { action = " replacement ", dueAt = "9999-12-31T23:59:59.9999999Z" }), ReminderEntry, "put", 204);
        await _app.DisposeAsync(); await Start(); _owner.Replace(_app.GetTestClient()); _other.Replace(_app.GetTestClient());
        var read = await Contract(await _owner.Send("GET", entry), ReminderEntry, "get", 200);
        Assert.That(read.GetProperty("dueAt").GetString(), Is.EqualTo("9999-12-31T23:59:59.9999999Z"));
        var action = await Contract(await _owner.Send("GET", entry + "/action"), ReminderAction, "get", 200);
        Assert.That(action.GetProperty("action").GetString(), Is.EqualTo(" replacement "));
        await Contract(await _owner.Send("POST", entry + "/complete"), ReminderEntry + "/complete", "post", 204);
        await Contract(await _owner.Send("POST", entry + "/complete"), ReminderEntry + "/complete", "post", 204);
        var conflict = await Contract(await _owner.Send("POST", entry + "/cancel"), ReminderEntry + "/cancel", "post", 409);
        Assert.That(conflict.GetProperty("code").GetString(), Is.EqualTo("not_pending"));
        var terminal = await Contract(await _owner.Send("PUT", entry, new { action = " ", dueAt = Due }), ReminderEntry, "put", 409);
        Assert.That(terminal.GetProperty("code").GetString(), Is.EqualTo("not_pending"));
        await Contract(await _owner.Send("POST", path, new { id, assetId = asset, action = "again", dueAt = Due }), Reminders, "post", 409);
        var cancelled = Guid.NewGuid();
        await Contract(await _owner.Send("POST", path, new { id = cancelled, assetId = asset, action = "cancel", dueAt = "0001-01-01T00:00:00Z" }), Reminders, "post", 201);
        await Contract(await _owner.Send("POST", $"{path}/{cancelled:D}/cancel"), ReminderEntry + "/cancel", "post", 204);
        await Contract(await _owner.Send("POST", $"{path}/{cancelled:D}/cancel"), ReminderEntry + "/cancel", "post", 204);
        var min = await Contract(await _owner.Send("GET", $"{path}/{cancelled:D}"), ReminderEntry, "get", 200);
        Assert.That(min.GetProperty("dueAt").GetString(), Is.EqualTo("0001-01-01T00:00:00.0000000Z"));
        Assert.That(min.GetProperty("status").GetString(), Is.EqualTo("cancelled"));
    }
    [TestCase(null)]
    [TestCase("2030-01-02")]
    [TestCase("2030-01-02T03:04:05")]
    [TestCase("2030-01-02T03:04:05.12345678Z")]
    [TestCase("0001-01-01T00:00:00+01:00")]
    [TestCase("9999-12-31T23:59:59-01:00")]
    [TestCase("2030-02-30T03:04:05Z")]
    [TestCase("2030-01-02T03:04:05Z\n")]
    [TestCase("2030-01-02T03:04:05+15:00")]
    public async Task ReminderTimestampRejectsAmbiguityAndPrecisionLoss(string? dueAt)
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault);
        var response = await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/reminders",
            new { id = Guid.NewGuid(), assetId = asset, action = "private fictional", dueAt }), Reminders, "post", 400);
        Assert.That(response.GetProperty("code").GetString(), Is.EqualTo("invalid_due_at"));
        Assert.That(response.GetRawText(), Does.Not.Contain("private fictional"));
    }
    [TestCase("owner")]
    [TestCase("administrator")]
    [TestCase("editor")]
    [TestCase("viewer")]
    public async Task ReminderRolesArchiveAndRevocation(string role)
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault); var id = Guid.NewGuid(); var path = $"{Vaults}/{vault:D}/reminders"; var entry = $"{path}/{id:D}";
        var body = new { id, assetId = asset, action = "fictional", dueAt = Due };
        await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/members", new { targetActorId = _otherId, role }), Members, "post", 204);
        await Contract(await _other.Send("POST", path, body), Reminders, "post", role == "viewer" ? 403 : 201);
        if (role == "viewer") await Contract(await _owner.Send("POST", path, body), Reminders, "post", 201);
        await Contract(await _other.Send("PUT", entry, new { action = "new", dueAt = Due }), ReminderEntry, "put", role == "viewer" ? 403 : 204);
        await Contract(await _other.Send("POST", entry + "/complete"), ReminderEntry + "/complete", "post", role == "viewer" ? 403 : 204);
        await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/archive"), Archival, "post", 204);
        await Contract(await _other.Send("GET", entry), ReminderEntry, "get", 200);
        await Contract(await _other.Send("GET", entry + "/action"), ReminderAction, "get", 200);
        await Contract(await _other.Send("POST", path, body), Reminders, "post", role == "viewer" ? 403 : 409);
        await Contract(await _other.Send("PUT", entry, new { action = "new", dueAt = Due }), ReminderEntry, "put", role == "viewer" ? 403 : 409);
        foreach (var op in new[] { "complete", "cancel" }) await Contract(await _other.Send("POST", entry + "/" + op), ReminderEntry + "/" + op, "post", role == "viewer" ? 403 : 409);
        await Sql($"DELETE FROM Memberships WHERE VaultId = {vault} AND ActorId = {_otherId}");
        await Contract(await _other.Send("GET", entry), ReminderEntry, "get", 404);
        await Contract(await _other.Send("GET", entry + "/action"), ReminderAction, "get", 404);
    }
    [TestCase("create")]
    [TestCase("update")]
    [TestCase("complete")]
    [TestCase("cancel")]
    [TestCase("read")]
    [TestCase("action")]
    public async Task ReminderAuthenticationIsolationAndAntiforgery(string op)
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault); var id = Guid.NewGuid(); var path = $"{Vaults}/{vault:D}/reminders";
        var body = new { id, assetId = asset, action = "fictional", dueAt = Due };
        await Contract(await _owner.Send("POST", path, body), Reminders, "post", 201);
        var suffix = op == "create" ? "" : $"/{id:D}" + (op is "complete" or "cancel" or "action" ? "/" + op : "");
        var template = op == "create" ? Reminders : ReminderEntry + (op is "complete" or "cancel" or "action" ? "/" + op : "");
        var method = op is "read" or "action" ? "GET" : op == "update" ? "PUT" : "POST";
        object? input = op == "create" ? body : op == "update" ? new { action = "new", dueAt = Due } : null;
        using var anonymous = new Browser(_app.GetTestClient());
        await Contract(await anonymous.Send(method, path + suffix, input), template, method.ToLowerInvariant(), 401);
        var denied = await Contract(await _other.Send(method, path + suffix, input, actorHeader: _ownerId), template, method.ToLowerInvariant(), 404);
        var missing = await Contract(await _other.Send(method, $"{Vaults}/{Guid.NewGuid():D}/reminders" + suffix, input), template, method.ToLowerInvariant(), 404);
        Assert.That(denied.GetRawText(), Is.EqualTo(missing.GetRawText()));
        if (method != "GET")
        {
            await Contract(await _owner.Send(method, path + suffix, input, csrf: false), template, method.ToLowerInvariant(), 400);
            await Contract(await _owner.Send(method, path + suffix, new { actorId = _otherId }), template, method.ToLowerInvariant(), 400);
        }
    }
    [TestCase("create", false)]
    [TestCase("update", false)]
    [TestCase("complete", false)]
    [TestCase("cancel", false)]
    [TestCase("create", true)]
    [TestCase("update", true)]
    [TestCase("complete", true)]
    [TestCase("cancel", true)]
    public async Task ReminderPermissionIsRecheckedAfterAuthentication(string op, bool archive)
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault); var id = Guid.NewGuid(); var path = $"{Vaults}/{vault:D}/reminders";
        var body = new { id, assetId = asset, action = "fictional", dueAt = Due };
        if (op != "create") await Contract(await _owner.Send("POST", path, body), Reminders, "post", 201);
        _gate.Enabled = true;
        var suffix = op == "create" ? "" : $"/{id:D}" + (op == "update" ? "" : "/" + op);
        var template = op == "create" ? Reminders : ReminderEntry + (op == "update" ? "" : "/" + op);
        var method = op == "update" ? "PUT" : "POST";
        var pending = _owner.Send(method, path + suffix, op == "create" ? body : op == "update" ? new { action = "new", dueAt = Due } : null);
        await _gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            if (archive) await Sql($"UPDATE Vaults SET Status = {1} WHERE Id = {vault}");
            else
            {
                await Sql($"INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ({vault}, {_otherId}, {0})");
                await Sql($"UPDATE Memberships SET Role = {3} WHERE VaultId = {vault} AND ActorId = {_ownerId}");
            }
        }
        finally { _gate.Release.TrySetResult(); }
        await Contract(await pending, template, method.ToLowerInvariant(), archive ? 409 : 403);
        var read = await Contract(await _owner.Send("GET", $"{path}/{id:D}"), ReminderEntry, "get", op == "create" ? 404 : 200);
        if (op != "create") Assert.That(read.GetProperty("status").GetString(), Is.EqualTo("pending"));
    }
    [Test]
    public async Task ReminderForeignAssetsAndCorruptOwnershipFailSafely()
    {
        var vault = await CreateVault(); var other = await CreateVault(); var asset = await CreateAsset(vault); var foreign = await CreateAsset(other); var id = Guid.NewGuid();
        var path = $"{Vaults}/{vault:D}/reminders";
        await Contract(await _owner.Send("POST", path, new { id, assetId = foreign, action = "fictional", dueAt = Due }), Reminders, "post", 404);
        var blank = await Contract(await _owner.Send("POST", path, new { id, assetId = asset, action = " ", dueAt = Due }), Reminders, "post", 400);
        Assert.That(blank.GetProperty("code").GetString(), Is.EqualTo("blank_action"));
        await Contract(await _owner.Send("POST", path, new { id, assetId = asset, action = "fictional", dueAt = Due }), Reminders, "post", 201);
        await Contract(await _owner.Send("GET", $"{Vaults}/{other:D}/reminders/{id:D}"), ReminderEntry, "get", 404);
        await Sql($"UPDATE Assets SET VaultId = {other} WHERE Id = {asset}");
        await Contract(await _owner.Send("GET", $"{path}/{id:D}"), ReminderEntry, "get", 500);
        await Contract(await _owner.Send("GET", $"{path}/{id:D}/action"), ReminderAction, "get", 500);
        await Contract(await _owner.Send("POST", $"{path}/{id:D}/cancel"), ReminderEntry + "/cancel", "post", 500);
    }
    private sealed class GatedReminderStore(IReminderStore inner, RegistrationGate gate) : IReminderStore
    {
        private async Task<ReminderOutcome> Invoke(Func<Task<ReminderOutcome>> operation, CancellationToken token)
        {
            if (gate.Enabled) { gate.Entered.TrySetResult(); await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(10), token); }
            return await operation();
        }
        public Task<ReminderOutcome> CreateAsync(Guid vaultId, Guid actorId, Guid id, Guid assetId, string? action, DateTimeOffset dueAt, CancellationToken token) => Invoke(() => inner.CreateAsync(vaultId, actorId, id, assetId, action, dueAt, token), token);
        public Task<ReminderOutcome> UpdateAsync(Guid vaultId, Guid actorId, Guid id, string? action, DateTimeOffset dueAt, CancellationToken token) => Invoke(() => inner.UpdateAsync(vaultId, actorId, id, action, dueAt, token), token);
        public Task<ReminderOutcome> CompleteAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token) => Invoke(() => inner.CompleteAsync(vaultId, actorId, id, token), token);
        public Task<ReminderOutcome> CancelAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token) => Invoke(() => inner.CancelAsync(vaultId, actorId, id, token), token);
        public Task<ReminderSnapshot?> FindAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken token) => inner.FindAsync(vaultId, actorId, id, token);
    }
}
