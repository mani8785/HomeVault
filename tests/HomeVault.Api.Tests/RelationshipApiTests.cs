using HomeVault.Application.Relationships;
using HomeVault.Domain.Relationships;
using Microsoft.AspNetCore.TestHost;
using NUnit.Framework;

namespace HomeVault.Api.Tests;

public sealed partial class RecordsApiTests
{
    private const string Relationships = "/api/v1/vaults/{vaultId}/relationships";
    private const string RelationshipEntry = Relationships + "/{relationshipId}";

    [Test]
    public async Task RelationshipJourneyRetainsRemovedRootAndEndpointsAcrossRestart()
    {
        var vault = await CreateVault(); var source = await CreateAsset(vault); var target = await CreateAsset(vault); var id = Guid.NewGuid();
        var path = $"{Vaults}/{vault:D}/relationships";
        var response = await _owner.Send("POST", path, new { id, sourceAssetId = source, targetAssetId = target, kind = "covers" });
        var created = await Contract(response, Relationships, "post", 201);
        Assert.That(response.Headers.Location!.OriginalString, Is.EqualTo($"{path}/{id:D}"));
        Assert.That(created.GetProperty("sourceAssetId").GetGuid(), Is.EqualTo(source));
        var duplicate = await Contract(await _owner.Send("POST", path, new { id = Guid.NewGuid(), sourceAssetId = source, targetAssetId = target, kind = "covers" }), Relationships, "post", 409);
        Assert.That(duplicate.GetProperty("code").GetString(), Is.EqualTo("relationship_exists"));
        await Contract(await _owner.Send("DELETE", $"{path}/{id:D}"), RelationshipEntry, "delete", 204);
        await Contract(await _owner.Send("DELETE", $"{path}/{id:D}"), RelationshipEntry, "delete", 204);
        await _app.DisposeAsync(); await Start();
        _owner.Replace(_app.GetTestClient()); _other.Replace(_app.GetTestClient());
        var read = await Contract(await _owner.Send("GET", $"{path}/{id:D}"), RelationshipEntry, "get", 200);
        Assert.That(read.GetProperty("status").GetString(), Is.EqualTo("removed"));
        Assert.That(read.GetProperty("targetAssetId").GetGuid(), Is.EqualTo(target));
        var conflict = await Contract(await _owner.Send("POST", path, new { id, sourceAssetId = source, targetAssetId = target, kind = "covers" }), Relationships, "post", 409);
        Assert.That(conflict.GetProperty("code").GetString(), Is.EqualTo("identity_conflict"));
        await Contract(await _owner.Send("POST", path, new { id = Guid.NewGuid(), sourceAssetId = source, targetAssetId = target, kind = "covers" }), Relationships, "post", 201);
        await Contract(await _owner.Send("GET", $"/api/v1/assets/{source:D}"), Inspection, "get", 200);
        await Contract(await _owner.Send("GET", $"/api/v1/assets/{target:D}"), Inspection, "get", 200);
    }

    [TestCase("owner", 201)]
    [TestCase("administrator", 201)]
    [TestCase("editor", 201)]
    [TestCase("viewer", 403)]
    public async Task RelationshipRolesArchiveAndRevocation(string role, int expected)
    {
        var vault = await CreateVault(); var source = await CreateAsset(vault); var target = await CreateAsset(vault); var id = Guid.NewGuid();
        var path = $"{Vaults}/{vault:D}/relationships";
        var body = new { id, sourceAssetId = source, targetAssetId = target, kind = "covers" };
        await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/members", new { targetActorId = _otherId, role }), Members, "post", 204);
        await Contract(await _other.Send("POST", path, body), Relationships, "post", expected);
        if (expected == 403) await Contract(await _owner.Send("POST", path, body), Relationships, "post", 201);
        await Contract(await _other.Send("DELETE", $"{path}/{id:D}"), RelationshipEntry, "delete", expected == 201 ? 204 : 403);
        await Contract(await _owner.Send("DELETE", $"{path}/{id:D}"), RelationshipEntry, "delete", 204);
        await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/archive"), Archival, "post", 204);
        await Contract(await _other.Send("GET", $"{path}/{id:D}"), RelationshipEntry, "get", 200);
        await Contract(await _other.Send("POST", path, body), Relationships, "post", expected == 201 ? 409 : 403);
        await Contract(await _other.Send("DELETE", $"{path}/{id:D}"), RelationshipEntry, "delete", expected == 201 ? 409 : 403);
        await Sql($"DELETE FROM Memberships WHERE VaultId = {vault} AND ActorId = {_otherId}");
        await Contract(await _other.Send("GET", $"{path}/{id:D}"), RelationshipEntry, "get", 404);
    }

    [Test]
    public async Task CrossVaultEndpointsFailEvenForSharedOwnerAndCorruptOwnershipFailsSafely()
    {
        var vault = await CreateVault(); var otherVault = await CreateVault();
        var source = await CreateAsset(vault); var target = await CreateAsset(vault); var foreign = await CreateAsset(otherVault);
        var id = Guid.NewGuid(); var path = $"{Vaults}/{vault:D}/relationships";
        var cross = await Contract(await _owner.Send("POST", path, new { id, sourceAssetId = source, targetAssetId = foreign, kind = "covers" }), Relationships, "post", 404);
        var missing = await Contract(await _owner.Send("POST", path, new { id, sourceAssetId = source, targetAssetId = Guid.NewGuid(), kind = "covers" }), Relationships, "post", 404);
        Assert.That(cross.GetRawText(), Is.EqualTo(missing.GetRawText()));
        await Contract(await _owner.Send("POST", path, new { id, sourceAssetId = foreign, targetAssetId = target, kind = "covers" }), Relationships, "post", 404);
        await Contract(await _owner.Send("POST", path, new { id, sourceAssetId = source, targetAssetId = target, kind = "covers" }), Relationships, "post", 201);
        await Contract(await _owner.Send("GET", $"{Vaults}/{otherVault:D}/relationships/{id:D}"), RelationshipEntry, "get", 404);
        await Sql($"UPDATE Assets SET VaultId = {otherVault} WHERE Id = {target}");
        await Contract(await _owner.Send("GET", $"{path}/{id:D}"), RelationshipEntry, "get", 500);
        await Contract(await _owner.Send("DELETE", $"{path}/{id:D}"), RelationshipEntry, "delete", 500);
    }

    [TestCase("POST")]
    [TestCase("GET")]
    [TestCase("DELETE")]
    public async Task RelationshipAuthenticationAntiforgerySpoofingAndMissingEquivalence(string method)
    {
        var vault = await CreateVault(); var source = await CreateAsset(vault); var target = await CreateAsset(vault); var id = Guid.NewGuid();
        var root = $"{Vaults}/{vault:D}/relationships";
        var body = new { id, sourceAssetId = source, targetAssetId = target, kind = "covers" };
        await Contract(await _owner.Send("POST", root, body), Relationships, "post", 201);
        var suffix = method == "POST" ? "" : $"/{id:D}";
        var contract = method == "POST" ? Relationships : RelationshipEntry;
        using var anonymous = new Browser(_app.GetTestClient());
        await Contract(await anonymous.Send(method, root + suffix, method == "POST" ? body : null), contract, method.ToLowerInvariant(), 401);
        var unavailable = await Contract(await _other.Send(method, root + suffix, method == "POST" ? body : null, actorHeader: _ownerId), contract, method.ToLowerInvariant(), 404);
        var missing = await Contract(await _other.Send(method, $"{Vaults}/{Guid.NewGuid():D}/relationships" + suffix, method == "POST" ? body : null), contract, method.ToLowerInvariant(), 404);
        Assert.That(unavailable.GetRawText(), Is.EqualTo(missing.GetRawText()));
        if (method != "GET")
        {
            await Contract(await _owner.Send(method, root + suffix, method == "POST" ? body : null, csrf: false), contract, method.ToLowerInvariant(), 400);
            await Contract(await _owner.Send(method, root + suffix, new { actorId = _otherId }), contract, method.ToLowerInvariant(), 400);
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task RelationshipRechecksPermissionAfterAuthentication(bool remove, bool archive)
    {
        var vault = await CreateVault(); var source = await CreateAsset(vault); var target = await CreateAsset(vault); var id = Guid.NewGuid();
        var path = $"{Vaults}/{vault:D}/relationships";
        var body = new { id, sourceAssetId = source, targetAssetId = target, kind = "covers" };
        if (remove) await Contract(await _owner.Send("POST", path, body), Relationships, "post", 201);
        _gate.Enabled = true;
        var pending = remove ? _owner.Send("DELETE", $"{path}/{id:D}") : _owner.Send("POST", path, body);
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
        await Contract(await pending, remove ? RelationshipEntry : Relationships, remove ? "delete" : "post", archive ? 409 : 403);
        var read = await Contract(await _owner.Send("GET", $"{path}/{id:D}"), RelationshipEntry, "get", remove ? 200 : 404);
        if (remove) Assert.That(read.GetProperty("status").GetString(), Is.EqualTo("active"));
    }

    [TestCase("covers", "self_reference")]
    [TestCase("unknown", "invalid_kind")]
    public async Task RelationshipValidationErrorsAreSafe(string kind, string code)
    {
        var vault = await CreateVault(); var source = await CreateAsset(vault);
        var error = await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/relationships",
            new { id = Guid.NewGuid(), sourceAssetId = source, targetAssetId = source, kind }), Relationships, "post", 400);
        Assert.That(error.GetProperty("code").GetString(), Is.EqualTo(code));
    }

    private sealed class GatedRelationshipStore(IRelationshipStore inner, RegistrationGate gate) : IRelationshipStore
    {
        private async Task<RelationshipOutcome> Invoke(Func<Task<RelationshipOutcome>> mutation, CancellationToken token)
        {
            if (gate.Enabled)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            }
            return await mutation();
        }
        public Task<RelationshipOutcome> CreateAsync(Guid vaultId, Guid actorId, Guid id, Guid sourceAssetId, Guid targetAssetId, RelationshipKind kind, CancellationToken cancellationToken) =>
            Invoke(() => inner.CreateAsync(vaultId, actorId, id, sourceAssetId, targetAssetId, kind, cancellationToken), cancellationToken);
        public Task<RelationshipOutcome> RemoveAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken cancellationToken) =>
            Invoke(() => inner.RemoveAsync(vaultId, actorId, id, cancellationToken), cancellationToken);
        public Task<RelationshipMetadata?> FindAsync(Guid vaultId, Guid actorId, Guid id, CancellationToken cancellationToken) => inner.FindAsync(vaultId, actorId, id, cancellationToken);
    }
}
