using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using NUnit.Framework;

namespace HomeVault.Api.Tests;

public sealed partial class RecordsApiTests
{
    [Test]
    public async Task LibraryPagesFilterBeforePagingAndRecheckMembership()
    {
        var vault = await CreateVault();
        var first = await CreateAsset(vault);
        var second = await CreateAsset(vault);
        var own = await _owner.Send("GET", "/api/v1/vaults?limit=1");
        var ownJson = JsonDocument.Parse(await own.Content.ReadAsStringAsync());
        Assert.That(ownJson.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid(), Is.EqualTo(vault));
        var other = await _other.Send("GET", "/api/v1/vaults");
        Assert.That(JsonDocument.Parse(await other.Content.ReadAsStringAsync()).RootElement.GetProperty("items").GetArrayLength(), Is.Zero);
        Assert.That((await _other.Send("GET", $"/api/v1/vaults/{vault}/assets")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var pageOne = JsonDocument.Parse(await (await _owner.Send("GET", $"/api/v1/vaults/{vault}/assets?limit=1")).Content.ReadAsStringAsync()).RootElement;
        var pageTwo = JsonDocument.Parse(await (await _owner.Send("GET", $"/api/v1/vaults/{vault}/assets?limit=1&offset=1")).Content.ReadAsStringAsync()).RootElement;
        Assert.That(pageOne.GetProperty("hasMore").GetBoolean(), Is.True);
        Assert.That(pageTwo.GetProperty("hasMore").GetBoolean(), Is.False);
        Assert.That(new[] { pageOne.GetProperty("items")[0].GetProperty("id").GetGuid(), pageTwo.GetProperty("items")[0].GetProperty("id").GetGuid() }, Is.EquivalentTo(new[] { first, second }));
        await Sql($"UPDATE Vaults SET Status = 1 WHERE Id = {vault}");
        Assert.That((await _owner.Send("GET", $"/api/v1/vaults/{vault}/assets")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await Sql($"DELETE FROM Memberships WHERE VaultId = {vault} AND ActorId = {_ownerId}");
        Assert.That((await _owner.Send("GET", $"/api/v1/vaults/{vault}/assets")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase("offset=-1")]
    [TestCase("limit=0")]
    [TestCase("limit=101")]
    [TestCase("offset=2147483648")]
    [TestCase("limit=abc")]
    public async Task LibraryRejectsInvalidPaging(string query)
    {
        Assert.That((await _owner.Send("GET", "/api/v1/vaults?" + query)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task MemberListsRequireCurrentAdministratorAndAnonymousListsFail()
    {
        var vault = await CreateVault();
        Assert.That((await _owner.Send("GET", $"/api/v1/vaults/{vault}/members")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await Sql($"UPDATE Memberships SET Role = 3 WHERE VaultId = {vault} AND ActorId = {_ownerId}");
        Assert.That((await _owner.Send("GET", $"/api/v1/vaults/{vault}/members")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        using var anonymous = _app.GetTestClient();
        Assert.That((await anonymous.GetAsync("/api/v1/vaults")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task LibraryReminderProjectionOmitsActionAndFiltersActualAsset()
    {
        var vault = await CreateVault(); var asset = await CreateAsset(vault); var another = await CreateAsset(vault);
        var id = Guid.NewGuid();
        var create = await _owner.Send("POST", $"/api/v1/vaults/{vault}/reminders", new { id, assetId = asset, action = "fictional private action", dueAt = "2027-01-01T12:00:00Z" });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var response = await _owner.Send("GET", $"/api/v1/vaults/{vault}/reminders?assetId={asset}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(text, Does.Not.Contain("fictional private action").And.Not.Contain("\"action\""));
        Assert.That(JsonDocument.Parse(text).RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
        var empty = await _owner.Send("GET", $"/api/v1/vaults/{vault}/reminders?assetId={another}");
        Assert.That(JsonDocument.Parse(await empty.Content.ReadAsStringAsync()).RootElement.GetProperty("items").GetArrayLength(), Is.Zero);
    }
}
