using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;
using Microsoft.AspNetCore.TestHost;
using NUnit.Framework;

namespace HomeVault.Api.Tests;

public sealed partial class RecordsApiTests
{
    private const string EvidenceRoute = "/api/v1/assets/{assetId}/evidence";
    private const string EvidenceEntry = EvidenceRoute + "/{evidenceId}";
    private const string EvidenceRead = EvidenceEntry + "/content";

    [Test]
    public async Task InvalidStoredContentProducesSafeFailuresAndMetadataStillOmitsContent()
    {
        var asset = await CreateAsset(await CreateVault()); var id = Guid.NewGuid();
        var path = $"/api/v1/assets/{asset:D}/evidence";
        await Contract(await _owner.Send("POST", path, new { id, label = "Label", kind = "note", content = "original" }), EvidenceRoute, "post", 201);
        await Sql($"UPDATE AssetEvidence SET Kind = {0}, Content = {"Private sentinel"} WHERE AssetId = {asset} AND Id = {id}");
        var listed = await Contract(await _owner.Send("GET", path), EvidenceRoute, "get", 200);
        Assert.That(listed.GetRawText(), Does.Not.Contain("Private sentinel"));
        await Contract(await _owner.Send("GET", $"{path}/{id:D}/content"), EvidenceRead, "get", 500);
        await Contract(await _owner.Send("DELETE", $"{path}/{id:D}"), EvidenceEntry, "delete", 500);
        await Contract(await _owner.Send("POST", path, new { id = Guid.NewGuid(), label = "Other", kind = "note", content = "text" }), EvidenceRoute, "post", 500);
    }

    [TestCase("note", " Private sentinel \nمتن ")]
    [TestCase("url", "https://example.invalid/a?token=fictional#part")]
    public async Task EvidenceJourneySeparatesMetadataFromContentAndSurvivesRestart(string kind, string content)
    {
        var asset = await CreateAsset(await CreateVault());
        var path = $"/api/v1/assets/{asset:D}/evidence";
        var id = Guid.NewGuid();
        var empty = await Contract(await _owner.Send("GET", path), EvidenceRoute, "get", 200);
        Assert.That(empty.GetProperty("evidence").GetArrayLength(), Is.Zero);
        var response = await _owner.Send("POST", path, new { id, label = " Label ", kind, content });
        var metadata = await Contract(response, EvidenceRoute, "post", 201);
        Assert.That(response.Headers.Location!.OriginalString, Is.EqualTo($"{path}/{id:D}/content"));
        Assert.That(metadata.TryGetProperty("content", out _), Is.False);
        Assert.That(metadata.GetProperty("label").GetString(), Is.EqualTo(" Label "));
        await Contract(await _owner.Send("POST", path, new { id, label = "Label", kind, content }), EvidenceRoute, "post", 409);
        await _app.DisposeAsync(); await Start();
        _owner.Replace(_app.GetTestClient()); _other.Replace(_app.GetTestClient());
        var listed = await Contract(await _owner.Send("GET", path), EvidenceRoute, "get", 200);
        Assert.That(listed.GetProperty("evidence")[0].TryGetProperty("content", out _), Is.False);
        var read = await Contract(await _owner.Send("GET", $"{path}/{id:D}/content"), EvidenceRead, "get", 200);
        Assert.That(read.GetProperty("content").GetString(), Is.EqualTo(content));
        await Contract(await _owner.Send("DELETE", $"{path}/{id:D}"), EvidenceEntry, "delete", 204);
        await Contract(await _owner.Send("DELETE", $"{path}/{id:D}"), EvidenceEntry, "delete", 404);
        await Contract(await _owner.Send("GET", $"{path}/{id:D}/content"), EvidenceRead, "get", 404);
        Assert.That(read.GetProperty("content").GetString(), Is.EqualTo(content));
    }

    [TestCase("owner", 201)]
    [TestCase("administrator", 201)]
    [TestCase("editor", 201)]
    [TestCase("viewer", 403)]
    public async Task EvidenceRolesArchivedReadsAndRevocation(string role, int expected)
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault); var id = Guid.NewGuid();
        var path = $"/api/v1/assets/{asset:D}/evidence";
        await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/members", new { targetActorId = _otherId, role }), Members, "post", 204);
        await Contract(await _other.Send("POST", path, new { id, label = "Label", kind = "note", content = "text" }), EvidenceRoute, "post", expected);
        if (expected == 403) await Contract(await _owner.Send("POST", path, new { id, label = "Label", kind = "note", content = "text" }), EvidenceRoute, "post", 201);
        await Contract(await _other.Send("DELETE", $"{path}/{id:D}"), EvidenceEntry, "delete", expected == 201 ? 204 : 403);
        if (expected == 201) await Contract(await _owner.Send("POST", path, new { id, label = "Label", kind = "note", content = "text" }), EvidenceRoute, "post", 201);
        await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/archive"), Archival, "post", 204);
        await Contract(await _other.Send("GET", path), EvidenceRoute, "get", 200);
        await Contract(await _other.Send("GET", $"{path}/{id:D}/content"), EvidenceRead, "get", 200);
        await Contract(await _other.Send("POST", path, new { id = Guid.NewGuid(), label = "Label", kind = "note", content = "text" }), EvidenceRoute, "post", expected == 201 ? 409 : 403);
        await Contract(await _other.Send("DELETE", $"{path}/{id:D}"), EvidenceEntry, "delete", expected == 201 ? 409 : 403);
        await Sql($"DELETE FROM Memberships WHERE VaultId = {vault} AND ActorId = {_otherId}");
        await Contract(await _other.Send("GET", path), EvidenceRoute, "get", 404);
        await Contract(await _other.Send("GET", $"{path}/{id:D}/content"), EvidenceRead, "get", 404);
    }

    [TestCase("POST")]
    [TestCase("DELETE")]
    [TestCase("LIST")]
    [TestCase("CONTENT")]
    public async Task EvidenceAuthenticationAndCrossVaultIsolation(string operation)
    {
        var asset = await CreateAsset(await CreateVault()); var id = Guid.NewGuid();
        var root = $"/api/v1/assets/{asset:D}/evidence";
        var suffix = operation == "DELETE" ? $"/{id:D}" : operation == "CONTENT" ? $"/{id:D}/content" : "";
        var contract = operation == "DELETE" ? EvidenceEntry : operation == "CONTENT" ? EvidenceRead : EvidenceRoute;
        var method = operation is "LIST" or "CONTENT" ? "GET" : operation;
        object? body = operation == "POST" ? new { id, label = "Label", kind = "note", content = "Private sentinel" } : null;
        await Contract(await _owner.Send("POST", root, new { id, label = "Label", kind = "note", content = "Private sentinel" }), EvidenceRoute, "post", 201);
        using var anonymous = new Browser(_app.GetTestClient());
        await Contract(await anonymous.Send(method, root + suffix, body), contract, method.ToLowerInvariant(), 401);
        var unavailable = await Contract(await _other.Send(method, root + suffix, body, actorHeader: _ownerId), contract, method.ToLowerInvariant(), 404);
        var missing = await Contract(await _other.Send(method, $"/api/v1/assets/{Guid.NewGuid():D}/evidence" + suffix, body), contract, method.ToLowerInvariant(), 404);
        Assert.That(unavailable.GetRawText(), Is.EqualTo(missing.GetRawText()));
        if (method != "GET")
        {
            await Contract(await _owner.Send(method, root + suffix, body, csrf: false), contract, method.ToLowerInvariant(), 400);
            await Contract(await _owner.Send(method, root + suffix, new { id, label = "Label", kind = "note", content = "Private sentinel", actorId = _otherId }), contract, method.ToLowerInvariant(), 400);
        }
    }

    [TestCase(" ", "note", "text", "blank_label")]
    [TestCase("Label", "file", "text", "invalid_kind")]
    [TestCase("Label", "note", " ", "blank_content")]
    [TestCase("Label", "url", "file:///Private sentinel", "invalid_url")]
    [TestCase("Label", "url", "https://user:password@example.invalid/", "invalid_url")]
    public async Task EvidenceValidationHasSafeErrors(string label, string kind, string content, string code)
    {
        var asset = await CreateAsset(await CreateVault()); var path = $"/api/v1/assets/{asset:D}/evidence";
        var error = await Contract(await _owner.Send("POST", path, new { id = Guid.NewGuid(), label, kind, content }), EvidenceRoute, "post", 400);
        Assert.That(error.GetProperty("code").GetString(), Is.EqualTo(code));
        var listed = await Contract(await _owner.Send("GET", path), EvidenceRoute, "get", 200);
        Assert.That(listed.GetProperty("evidence").GetArrayLength(), Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task EvidenceRechecksAccessAfterAuthentication(bool remove, bool archive)
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault); var id = Guid.NewGuid();
        var path = $"/api/v1/assets/{asset:D}/evidence";
        await Contract(await _owner.Send("POST", path, new { id, label = "Label", kind = "note", content = "original" }), EvidenceRoute, "post", 201);
        _gate.Enabled = true;
        var pending = remove ? _owner.Send("DELETE", $"{path}/{id:D}") : _owner.Send("POST", path, new { id = Guid.NewGuid(), label = "Other", kind = "note", content = "new" });
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
        await Contract(await pending, remove ? EvidenceEntry : EvidenceRoute, remove ? "delete" : "post", archive ? 409 : 403);
        var read = await Contract(await _owner.Send("GET", $"{path}/{id:D}/content"), EvidenceRead, "get", 200);
        Assert.That(read.GetProperty("content").GetString(), Is.EqualTo("original"));
        var listed = await Contract(await _owner.Send("GET", path), EvidenceRoute, "get", 200);
        Assert.That(listed.GetProperty("evidence").GetArrayLength(), Is.EqualTo(1));
    }

    private sealed class GatedEvidenceStore(IEvidenceStore inner, RegistrationGate gate) : IEvidenceStore
    {
        private async Task<EvidenceOutcome> Invoke(Func<Task<EvidenceOutcome>> mutation, CancellationToken token)
        {
            if (gate.Enabled)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            }
            return await mutation();
        }
        public Task<EvidenceOutcome> AddAsync(Guid assetId, Guid actorId, Guid id, string? label, EvidenceKind kind, string? content, CancellationToken cancellationToken) =>
            Invoke(() => inner.AddAsync(assetId, actorId, id, label, kind, content, cancellationToken), cancellationToken);
        public Task<EvidenceOutcome> RemoveAsync(Guid assetId, Guid actorId, Guid id, CancellationToken cancellationToken) =>
            Invoke(() => inner.RemoveAsync(assetId, actorId, id, cancellationToken), cancellationToken);
        public Task<IReadOnlyList<EvidenceMetadata>?> ListAsync(Guid assetId, Guid actorId, CancellationToken cancellationToken) => inner.ListAsync(assetId, actorId, cancellationToken);
        public Task<EvidenceContent?> ReadContentAsync(Guid assetId, Guid actorId, Guid id, CancellationToken cancellationToken) => inner.ReadContentAsync(assetId, actorId, id, cancellationToken);
    }
}
