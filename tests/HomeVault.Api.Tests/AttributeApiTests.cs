using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using Microsoft.AspNetCore.TestHost;
using NUnit.Framework;

namespace HomeVault.Api.Tests;

public sealed partial class RecordsApiTests
{
    private const string Attributes = "/api/v1/assets/{assetId}/attributes";

    [TestCase("add", false)]
    [TestCase("change", false)]
    [TestCase("remove", false)]
    [TestCase("add", true)]
    [TestCase("change", true)]
    [TestCase("remove", true)]
    public async Task AttributeRequestsRecheckPermissionAndArchiveAfterAuthentication(string operation, bool archive)
    {
        var vault = await CreateVault();
        var asset = await CreateAsset(vault);
        var path = $"/api/v1/assets/{asset:D}/attributes";
        await Contract(await _owner.Send("POST", path + "/add", new { name = "Label", value = "original", sensitivity = "ordinary" }), Attributes + "/add", "post", 204);
        _gate.Enabled = true;
        object body = operation == "add" ? new { name = "Other", value = "new", sensitivity = "ordinary" }
            : operation == "change" ? new { name = "Label", value = "new" } : new { name = "Label" };
        var pending = _owner.Send("POST", path + "/" + operation, body);
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
        await Contract(await pending, Attributes + "/" + operation, "post", archive ? 409 : 403);
        var read = await Contract(await _owner.Send("GET", path), Attributes, "get", 200);
        Assert.That(read.GetProperty("attributes").GetArrayLength(), Is.EqualTo(1));
        Assert.That(read.GetProperty("attributes")[0].GetProperty("value").GetString(), Is.EqualTo("original"));
    }

    private sealed class GatedAttributeStore(IOrdinaryAttributeStore inner, RegistrationGate gate) : IOrdinaryAttributeStore
    {
        private async Task<AttributeOutcome> Invoke(Func<Task<AttributeOutcome>> mutation, CancellationToken token)
        {
            if (gate.Enabled)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            }
            return await mutation();
        }
        public Task<AttributeOutcome> AddAsync(Guid assetId, Guid actorId, string? name, string? value, AttributeSensitivity sensitivity, CancellationToken cancellationToken) =>
            Invoke(() => inner.AddAsync(assetId, actorId, name, value, sensitivity, cancellationToken), cancellationToken);
        public Task<AttributeOutcome> ChangeAsync(Guid assetId, Guid actorId, string? name, string? value, CancellationToken cancellationToken) =>
            Invoke(() => inner.ChangeAsync(assetId, actorId, name, value, cancellationToken), cancellationToken);
        public Task<AttributeOutcome> RemoveAsync(Guid assetId, Guid actorId, string? name, CancellationToken cancellationToken) =>
            Invoke(() => inner.RemoveAsync(assetId, actorId, name, cancellationToken), cancellationToken);
        public Task<IReadOnlyList<AssetAttribute>?> ListAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken) => inner.ListAsync(assetId, actorId, cancellationToken);
    }

    [Test]
    public async Task OrdinaryAttributeHttpJourneyPreservesUnicodeSpellingAndSurvivesRestart()
    {
        var vault = await CreateVault();
        var asset = await CreateAsset(vault);
        var path = $"/api/v1/assets/{asset:D}/attributes";
        var empty = await Contract(await _owner.Send("GET", path), Attributes, "get", 200);
        Assert.That(empty.GetProperty("attributes").GetArrayLength(), Is.Zero);
        await Contract(await _owner.Send("POST", path + "/add", new { name = " Étage/طبقه ", value = " first ", sensitivity = "ordinary" }), Attributes + "/add", "post", 204);
        await Contract(await _owner.Send("POST", path + "/add", new { name = "étage/طبقه", value = "duplicate", sensitivity = "ordinary" }), Attributes + "/add", "post", 409);
        await _app.DisposeAsync();
        await Start();
        _owner.Replace(_app.GetTestClient());
        _other.Replace(_app.GetTestClient());
        var read = await Contract(await _owner.Send("GET", path), Attributes, "get", 200);
        Assert.That(read.GetProperty("attributes")[0].GetProperty("name").GetString(), Is.EqualTo("Étage/طبقه"));
        Assert.That(read.GetProperty("attributes")[0].GetProperty("value").GetString(), Is.EqualTo(" first "));
        await Contract(await _owner.Send("POST", path + "/change", new { name = "étage/طبقه", value = " second " }), Attributes + "/change", "post", 204);
        await Contract(await _owner.Send("POST", path + "/remove", new { name = "ÉTAGE/طبقه" }), Attributes + "/remove", "post", 204);
        await Contract(await _owner.Send("POST", path + "/remove", new { name = "ÉTAGE/طبقه" }), Attributes + "/remove", "post", 404);
        Assert.That(read.GetProperty("attributes")[0].GetProperty("value").GetString(), Is.EqualTo(" first "));
    }

    [TestCase("owner", 204)]
    [TestCase("administrator", 204)]
    [TestCase("editor", 204)]
    [TestCase("viewer", 403)]
    public async Task AttributeHttpRolesRevocationAndArchival(string role, int expected)
    {
        var vault = await CreateVault();
        var asset = await CreateAsset(vault);
        var path = $"/api/v1/assets/{asset:D}/attributes";
        var memberPath = $"{Vaults}/{vault:D}/members";
        var unavailable = await Contract(await _other.Send("GET", path), Attributes, "get", 404);
        var absent = await Contract(await _other.Send("GET", $"/api/v1/assets/{Guid.NewGuid():D}/attributes"), Attributes, "get", 404);
        Assert.That(unavailable.GetRawText(), Is.EqualTo(absent.GetRawText()));
        await Contract(await _owner.Send("POST", path + "/add", new { name = "Label", value = "original", sensitivity = "ordinary" }), Attributes + "/add", "post", 204);
        await Contract(await _owner.Send("POST", memberPath, new { targetActorId = _otherId, role }), Members, "post", 204);
        await Contract(await _other.Send("POST", path + "/add", new { name = "Other", value = "text", sensitivity = "ordinary" }), Attributes + "/add", "post", expected);
        await Contract(await _other.Send("POST", path + "/change", new { name = "Label", value = "text" }), Attributes + "/change", "post", expected);
        await Contract(await _other.Send("POST", path + "/remove", new { name = "Label" }), Attributes + "/remove", "post", expected);
        await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/archive"), Archival, "post", 204);
        await Contract(await _other.Send("GET", path), Attributes, "get", 200);
        var archived = expected == 204 ? 409 : 403;
        await Contract(await _other.Send("POST", path + "/add", new { name = "Denied", value = "text", sensitivity = "ordinary" }), Attributes + "/add", "post", archived);
        await Contract(await _other.Send("POST", path + "/change", new { name = "Label", value = "text" }), Attributes + "/change", "post", archived);
        await Contract(await _other.Send("POST", path + "/remove", new { name = "Label" }), Attributes + "/remove", "post", archived);
        await Sql($"DELETE FROM Memberships WHERE VaultId = {vault} AND ActorId = {_otherId}");
        await Contract(await _other.Send("GET", path), Attributes, "get", 404);
        await Contract(await _other.Send("POST", path + "/remove", new { name = "Label" }), Attributes + "/remove", "post", 404);
    }

    [TestCase("add")]
    [TestCase("change")]
    [TestCase("remove")]
    public async Task AttributeHttpRejectsAnonymousCsrfSpoofingAndCrossVaultRequests(string operation)
    {
        var asset = await CreateAsset(await CreateVault());
        var path = $"/api/v1/assets/{asset:D}/attributes/{operation}";
        var contract = Attributes + "/" + operation;
        object body = operation == "add" ? new { name = "Label", value = "Private sentinel", sensitivity = "ordinary" }
            : operation == "change" ? new { name = "Label", value = "Private sentinel" } : new { name = "Label" };
        using var anonymous = new Browser(_app.GetTestClient());
        await Contract(await anonymous.Send("POST", path, body), contract, "post", 401);
        await Contract(await _owner.Send("POST", path, body, csrf: false), contract, "post", 400);
        await Contract(await _owner.Send("POST", path, new { name = "Label", actorId = _otherId }), contract, "post", 400);
        var denied = await Contract(await _other.Send("POST", path, body, actorHeader: _ownerId), contract, "post", 404);
        var missing = await Contract(await _other.Send("POST", $"/api/v1/assets/{Guid.NewGuid():D}/attributes/{operation}", body), contract, "post", 404);
        Assert.That(denied.GetRawText(), Is.EqualTo(missing.GetRawText()));
    }

    [TestCase(null)]
    [TestCase("sensitive")]
    [TestCase("unknown")]
    public async Task AttributeHttpRejectsUnsupportedClassificationWithoutPersisting(string? sensitivity)
    {
        var asset = await CreateAsset(await CreateVault());
        var path = $"/api/v1/assets/{asset:D}/attributes";
        var problem = await Contract(await _owner.Send("POST", path + "/add", new { name = "Label", value = "Private sentinel", sensitivity }), Attributes + "/add", "post", 400);
        Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo("unsupported_sensitivity"));
        var read = await Contract(await _owner.Send("GET", path), Attributes, "get", 200);
        Assert.That(read.GetProperty("attributes").GetArrayLength(), Is.Zero);
    }
}
