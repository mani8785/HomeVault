using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using HomeVault.Infrastructure.Encryption;
using Microsoft.AspNetCore.TestHost;
using NUnit.Framework;

namespace HomeVault.Api.Tests;

public sealed partial class RecordsApiTests
{
    [TestCase(0), TestCase(1), TestCase(2), TestCase(3)]
    public async Task SensitiveHttpUsesRealCurrentMembershipAndNeverListsText(int role)
    {
        using var custody = new HttpFictionalCustody();
        using var encrypted = new SensitiveStorageSession(custody);
        await _app.DisposeAsync(); await Start(encrypted);
        _owner.Replace(_app.GetTestClient()); _other.Replace(_app.GetTestClient());
        var vault = await CreateVault(); var asset = await CreateAsset(vault);
        await Sql($"INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ({vault}, {_otherId}, {role})");
        var path = $"/api/v1/assets/{asset:D}/sensitive-attributes";
        const string value = " fictional-http-private-فارسی 😀 \t";
        var added = await _owner.Send("POST", path + "/add", new { name = "private", value, sensitivity = "sensitive" });
        Assert.That(added.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        await Contract(added, "/api/v1/assets/{assetId}/sensitive-attributes/add", "post", 201);
        var id = JsonDocument.Parse(await added.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        var metadata = await _other.Send("GET", path);
        Assert.That(metadata.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await metadata.Content.ReadAsStringAsync(), Does.Not.Contain("value").And.Not.Contain("fictional-http-private"));
        var read = await _other.Send("POST", path + $"/{id:D}/read");
        Assert.That(read.StatusCode, Is.EqualTo(role <= 1 ? HttpStatusCode.OK : HttpStatusCode.Forbidden));
        Assert.That(read.Headers.CacheControl?.NoStore, Is.True);
        await Contract(read, "/api/v1/assets/{assetId}/sensitive-attributes/{attributeId}/read", "post", role <= 1 ? 200 : 403);
        Assert.That((await _owner.Send("POST", path + $"/{id:D}/read", csrf: false)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        if (role <= 1) Assert.That(JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetString(), Is.EqualTo(value));
        else Assert.That(await read.Content.ReadAsStringAsync(), Does.Not.Contain("fictional-http-private"));
        var changed = await _other.Send("POST", path + $"/{id:D}/change", new { value = "new fictional value" });
        Assert.That(changed.StatusCode, Is.EqualTo(role <= 1 ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden));
        await Sql($"DELETE FROM Memberships WHERE VaultId = {vault} AND ActorId = {_otherId}");
        Assert.That((await _other.Send("POST", path + $"/{id:D}/read")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await _owner.Send("POST", path + $"/{id:D}/read", new { actorId = _otherId })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await _owner.Send("POST", path + "/add", new { name = "forged", value, sensitivity = "sensitive", actorId = _ownerId })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await _app.DisposeAsync(); await Start();
        _owner.Replace(_app.GetTestClient()); _other.Replace(_app.GetTestClient());
        Assert.That((await _owner.Send("GET", path)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private sealed class HttpFictionalCustody : IEncryptionKeyCustody, IDisposable
    {
        private readonly Dictionary<Guid, byte[]> _keys = [];
        public WriteKeySession CreateVerifiedWriteSession()
        { var id = Guid.NewGuid(); var key = RandomNumberGenerator.GetBytes(32); _keys.Add(id, key); return new WriteKeySession(id, key.ToArray()); }
        public ReadKeyLease? FindReadKey(Guid id) => _keys.TryGetValue(id, out var key) ? new ReadKeyLease(id, key.ToArray()) : null;
        public void Dispose() { foreach (var key in _keys.Values) CryptographicOperations.ZeroMemory(key); }
    }
}
