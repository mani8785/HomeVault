using System.Net.Http.Json;
using HomeVault.Application.Records;
using Microsoft.AspNetCore.TestHost;
using NUnit.Framework;

namespace HomeVault.Api.Tests;

public sealed partial class RecordsApiTests
{
    private const string VaultName = "/api/v1/vaults/{vaultId}/name";
    private const string AssetName = "/api/v1/assets/{assetId}/name";

    [Test]
    public async Task RenameExactNamesAndStaleConflictSurviveHostRestart()
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault);
        await Contract(await _owner.Send("PUT", $"/api/v1/vaults/{vault:D}/name", new { name = " Étage/طبقه ", expectedName = "Fictional Vault" }), VaultName, "put", 204);
        await Contract(await _owner.Send("PUT", $"/api/v1/assets/{asset:D}/name", new { name = " New Asset ", expectedName = "Fictional Asset" }), AssetName, "put", 204);
        var stale = await Contract(await _owner.Send("PUT", $"/api/v1/assets/{asset:D}/name", new { name = "Private sentinel", expectedName = "Fictional Asset" }), AssetName, "put", 409);
        Assert.That(stale.GetProperty("code").GetString(), Is.EqualTo("name_conflict"));
        await _app.DisposeAsync(); await Start(); _owner.Replace(_app.GetTestClient()); _other.Replace(_app.GetTestClient());
        var reloaded = await Contract(await _owner.Send("GET", $"/api/v1/assets/{asset:D}"), Inspection, "get", 200);
        Assert.That(reloaded.GetProperty("name").GetString(), Is.EqualTo(" New Asset "));
        Assert.That(reloaded.GetProperty("vaultId").GetGuid(), Is.EqualTo(vault));
        await Contract(await _owner.Send("PUT", $"/api/v1/assets/{asset:D}/name", new { name = " New Asset ", expectedName = " New Asset " }), AssetName, "put", 204);
    }

    [TestCase("owner", 204, 204), TestCase("administrator", 204, 204), TestCase("editor", 403, 204), TestCase("viewer", 403, 403)]
    public async Task RenameHttpRoleMatrixAndArchive(string role, int vaultExpected, int assetExpected)
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault);
        await Contract(await _owner.Send("POST", $"/api/v1/vaults/{vault:D}/members", new { targetActorId = _otherId, role }), Members, "post", 204);
        await Contract(await _other.Send("PUT", $"/api/v1/vaults/{vault:D}/name", new { name = "New Vault", expectedName = "Fictional Vault" }), VaultName, "put", vaultExpected);
        await Contract(await _other.Send("PUT", $"/api/v1/assets/{asset:D}/name", new { name = "New Asset", expectedName = "Fictional Asset" }), AssetName, "put", assetExpected);
        await Contract(await _owner.Send("POST", $"/api/v1/vaults/{vault:D}/archive"), Archival, "post", 204);
        await Contract(await _other.Send("PUT", $"/api/v1/vaults/{vault:D}/name", new { name = "Private sentinel", expectedName = "unknown" }), VaultName, "put", vaultExpected == 204 ? 409 : 403);
        await Contract(await _other.Send("PUT", $"/api/v1/assets/{asset:D}/name", new { name = "Private sentinel", expectedName = "unknown" }), AssetName, "put", assetExpected == 204 ? 409 : 403);
    }

    [TestCase(false), TestCase(true)]
    public async Task RenameIsolationInputAndAntiforgeryFailuresAreSafe(bool vaultRecord)
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault); var id = vaultRecord ? vault : asset;
        var prefix = vaultRecord ? "/api/v1/vaults/" : "/api/v1/assets/"; var contract = vaultRecord ? VaultName : AssetName;
        var path = prefix + id.ToString("D") + "/name";
        var input = new { name = "Private sentinel", expectedName = "Private sentinel" };
        var denied = await Contract(await _other.Send("PUT", path, input, actorHeader: _ownerId), contract, "put", 404);
        var missing = await Contract(await _other.Send("PUT", prefix + Guid.NewGuid().ToString("D") + "/name", input), contract, "put", 404);
        Assert.That(denied.GetRawText(), Is.EqualTo(missing.GetRawText()));
        await Contract(await _owner.Send("PUT", path, input, csrf: false), contract, "put", 400);
        await Contract(await _owner.Send("PUT", path, new { name = "Private sentinel", expectedName = "Original", actorId = _otherId }), contract, "put", 400);
        await Contract(await _owner.Send("PUT", path, new { name = " ", expectedName = "Original" }), contract, "put", 400);
        await Contract(await _owner.Send("PUT", path, new { name = "Private sentinel" }), contract, "put", 400);
        await Contract(await _owner.Send("PUT", prefix + "invalid/name", input), contract, "put", 400);
        using var anonymous = _app.GetTestClient();
        using var response = await anonymous.PutAsJsonAsync("https://localhost" + path, input);
        await Contract(response, contract, "put", 401);
        await Contract(await _owner.Send("POST", $"/api/v1/vaults/{vault:D}/members", new { targetActorId = _otherId, role = "editor" }), Members, "post", 204);
        await Contract(await _owner.Send("DELETE", $"/api/v1/vaults/{vault:D}/members/{_otherId:D}"), Member, "delete", 204);
        await Contract(await _other.Send("PUT", path, input), contract, "put", 404);
    }

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public async Task RenameRechecksCommittedRoleAndArchiveAfterAuthentication(bool vaultRecord, bool archive)
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault); _gate.Enabled = true;
        var path = vaultRecord ? $"/api/v1/vaults/{vault:D}/name" : $"/api/v1/assets/{asset:D}/name";
        var pending = _owner.Send("PUT", path, new { name = "Private sentinel", expectedName = vaultRecord ? "Fictional Vault" : "Fictional Asset" });
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
        await Contract(await pending, vaultRecord ? VaultName : AssetName, "put", archive ? 409 : 403);
    }

    private sealed class GatedNameStore(IRecordNameStore inner, RegistrationGate gate) : IRecordNameStore
    {
        private async Task<RenameOutcome> Invoke(Func<Task<RenameOutcome>> action, CancellationToken token)
        {
            if (gate.Enabled) { gate.Entered.TrySetResult(); await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(10), token); }
            return await action();
        }
        public Task<RenameOutcome> RenameVaultAsync(Guid id, Guid actor, string? name, string? expectedName, CancellationToken token) => Invoke(() => inner.RenameVaultAsync(id, actor, name, expectedName, token), token);
        public Task<RenameOutcome> RenameAssetAsync(Guid id, Guid actor, string? name, string? expectedName, CancellationToken token) => Invoke(() => inner.RenameAssetAsync(id, actor, name, expectedName, token), token);
    }
}
