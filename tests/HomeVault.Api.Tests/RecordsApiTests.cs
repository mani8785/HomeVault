using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeVault.Application.Assets;
using HomeVault.Application.Vaults;
using HomeVault.Domain.Assets;
using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace HomeVault.Api.Tests;

[TestFixture]
public sealed partial class RecordsApiTests
{
    private string _directory = null!;
    private string _path = null!;
    private WebApplication _app = null!;
    private IDataProtectionProvider _keys = null!;
    private Browser _owner = null!;
    private Browser _other = null!;
    private Guid _ownerId;
    private Guid _otherId;
    private JsonDocument _contract = null!;
    private readonly RegistrationGate _gate = new();
    private const string Vaults = "/api/v1/vaults";
    private const string Registration = "/api/v1/vaults/{vaultId}/assets";
    private const string Inspection = "/api/v1/assets/{assetId}";
    private const string Archival = "/api/v1/vaults/{vaultId}/archive";
    private const string Password = "fictional records passphrase";

    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVault-records-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "records.db");
        await new SqliteDatabase(_path).MigrateAsync();
        _keys = new EphemeralDataProtectionProvider();
        _gate.Reset();
        await Start();
        _owner = new Browser(_app.GetTestClient());
        _other = new Browser(_app.GetTestClient());
        _ownerId = await EnrollAndLogin(_owner, "owner@example.invalid");
        _otherId = await EnrollAndLogin(_other, "other@example.invalid");
        var contract = await _owner.Send("GET", "/api/v1/openapi.json");
        Assert.That(contract.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        _contract = JsonDocument.Parse(await contract.Content.ReadAsStringAsync());
        Assert.That(_contract.RootElement.GetProperty("openapi").GetString(), Is.EqualTo("3.0.3"));
    }

    private async Task Start()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        AuthenticationHost.Configure(builder.Services, _path, _keys);
        RecordsApi.Configure(builder.Services, _path);
        // Test-only barrier, delegating to the real SQLite adapter; no actor substitution.
        builder.Services.AddScoped<IAssetRegistrationStore>(services => new GatedRegistrationStore(
            new SqliteAssetRegistrationStore(services.GetRequiredService<SqliteDatabase>()), _gate));
        builder.Services.AddScoped<IVaultArchiveStore>(services => new GatedArchiveStore(
            new SqliteVaultArchiveStore(services.GetRequiredService<SqliteDatabase>()), _gate));
        builder.Services.AddScoped<IVaultMembershipStore>(services => new GatedMembershipStore(
            new SqliteVaultMembershipStore(services.GetRequiredService<SqliteDatabase>()), _gate));
        builder.Services.AddScoped<IOrdinaryAttributeStore>(services => new GatedAttributeStore(
            new SqliteOrdinaryAttributeStore(services.GetRequiredService<SqliteDatabase>()), _gate));
        builder.Services.AddScoped<IEvidenceStore>(services => new GatedEvidenceStore(
            new SqliteEvidenceStore(services.GetRequiredService<SqliteDatabase>()), _gate));
        _app = builder.Build();
        AuthenticationHost.Map(_app);
        RecordsApi.Map(_app);
        await _app.StartAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        _gate.Release.TrySetResult();
        _owner.Dispose();
        _other.Dispose();
        _contract.Dispose();
        await _app.DisposeAsync();
        Directory.Delete(_directory, true);
    }

    [TestCase("personal")]
    [TestCase("household")]
    [TestCase("organization")]
    public async Task OwnerCreatesAndReloadsExactMetadataAfterHostRestart(string type)
    {
        var response = await _owner.Send("POST", Vaults, new { name = " Fictional Vault ", type });
        var vault = await Contract(response, Vaults, "post", 201);
        Assert.That(response.Headers.Location, Is.Null);
        var id = vault.GetProperty("id").GetGuid();
        Assert.That(vault.GetProperty("name").GetString(), Is.EqualTo(" Fictional Vault "));
        Assert.That(vault.GetProperty("type").GetString(), Is.EqualTo(type));
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>();
            var members = await db.Database.SqlQuery<Guid>($"SELECT ActorId AS Value FROM Memberships WHERE VaultId = {id}").ToListAsync();
            Assert.That(members, Is.EqualTo(new[] { _ownerId }));
        }
        const string name = " <fictional & bicycle> ";
        var created = await _owner.Send("POST", $"{Vaults}/{id:D}/assets", new { name });
        var asset = await Contract(created, Registration, "post", 201);
        var assetId = asset.GetProperty("id").GetGuid();
        Assert.That(asset.GetProperty("vaultId").GetGuid(), Is.EqualTo(id));
        Assert.That(created.Headers.Location?.ToString(), Is.EqualTo($"/api/v1/assets/{assetId:D}"));
        await _app.DisposeAsync();
        await Start();
        _owner.Replace(_app.GetTestClient());
        _other.Replace(_app.GetTestClient());
        var reread = await Contract(await _owner.Send("GET", $"/api/v1/assets/{assetId:D}"), Inspection, "get", 200);
        Assert.That(reread.GetProperty("name").GetString(), Is.EqualTo(name));
    }

    [TestCase(0, false, 201)]
    [TestCase(1, false, 201)]
    [TestCase(2, false, 201)]
    [TestCase(3, false, 403)]
    [TestCase(0, true, 409)]
    [TestCase(1, true, 409)]
    [TestCase(2, true, 409)]
    [TestCase(3, true, 403)]
    public async Task AllRolesReadButOnlyActiveWritersRegister(int role, bool archived, int expected)
    {
        var vault = await CreateVault();
        var asset = await CreateAsset(vault);
        await Sql($"INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ({vault}, {_otherId}, {role})");
        if (archived) await Sql($"UPDATE Vaults SET Status = {1} WHERE Id = {vault}");
        await Contract(await _other.Send("GET", $"/api/v1/assets/{asset:D}"), Inspection, "get", 200);
        var response = await _other.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Another fictional Asset" });
        var payload = await Contract(response, Registration, "post", expected);
        if (expected == 409) Assert.That(payload.GetProperty("code").GetString(), Is.EqualTo("vault_archived"));
        Assert.That(await AssetCount(), Is.EqualTo(expected == 201 ? 2 : 1));
    }

    [Test]
    public async Task MissingAndInaccessibleAreIdenticalAndPermissionChangesApplyWithoutRelogin()
    {
        var vault = await CreateVault();
        var asset = await CreateAsset(vault);
        var denied = await Contract(await _other.Send("GET", $"/api/v1/assets/{asset:D}"), Inspection, "get", 404);
        var missing = await Contract(await _other.Send("GET", $"/api/v1/assets/{Guid.NewGuid():D}"), Inspection, "get", 404);
        Assert.That(denied.GetRawText(), Is.EqualTo(missing.GetRawText()));
        var deniedWrite = await Contract(await _other.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Fictional" }), Registration, "post", 404);
        var missingWrite = await Contract(await _other.Send("POST", $"{Vaults}/{Guid.NewGuid():D}/assets", new { name = "Fictional" }), Registration, "post", 404);
        Assert.That(deniedWrite.GetRawText(), Is.EqualTo(missingWrite.GetRawText()));
        await Sql($"INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ({vault}, {_otherId}, {2})");
        await Contract(await _other.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Allowed" }), Registration, "post", 201);
        await Sql($"UPDATE Memberships SET Role = {3} WHERE VaultId = {vault} AND ActorId = {_otherId}");
        await Contract(await _other.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Denied" }), Registration, "post", 403);
        await Sql($"DELETE FROM Memberships WHERE VaultId = {vault} AND ActorId = {_otherId}");
        await Contract(await _other.Send("GET", $"/api/v1/assets/{asset:D}"), Inspection, "get", 404);
    }

    [Test]
    public async Task ForgedActorsAndIdsCannotChangeOwnershipOrBypassAccess()
    {
        var vault = await CreateVault();
        var asset = await CreateAsset(vault);
        await Contract(await _other.Send("POST", Vaults, new { name = "Private sentinel", type = "personal", ownerId = _ownerId }), Vaults, "post", 400);
        await Contract(await _other.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Private sentinel", actorId = _ownerId, id = asset }), Registration, "post", 400);
        var query = $"?actorId={_ownerId:D}&ownerId={_ownerId:D}";
        var response = await _other.Send("POST", Vaults + query, new { name = "Other Vault", type = "personal" }, actorHeader: _ownerId);
        var ownVault = (await Contract(response, Vaults, "post", 201)).GetProperty("id").GetGuid();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>();
            Assert.That(await db.Database.SqlQuery<Guid>($"SELECT ActorId AS Value FROM Memberships WHERE VaultId = {ownVault}").SingleAsync(), Is.EqualTo(_otherId));
        }
        await Contract(await _other.Send("GET", $"/api/v1/assets/{asset:D}" + query, actorHeader: _ownerId), Inspection, "get", 404);
        await Contract(await _other.Send("POST", $"{Vaults}/{vault:D}/assets" + query, new { name = "Denied" }, actorHeader: _ownerId), Registration, "post", 404);
        Assert.That(await AssetCount(), Is.EqualTo(1));
    }

    [Test]
    public async Task AnonymousAndRevokedCookiesFailAndWritesRequireAntiforgery()
    {
        using var anonymous = new Browser(_app.GetTestClient());
        var vault = await CreateVault();
        var asset = await CreateAsset(vault);
        await Contract(await anonymous.Send("POST", Vaults, new { name = "Denied", type = "personal" }), Vaults, "post", 401);
        await Contract(await anonymous.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Denied" }), Registration, "post", 401);
        await Contract(await anonymous.Send("GET", $"/api/v1/assets/{asset:D}"), Inspection, "get", 401);
        await Contract(await _owner.Send("POST", Vaults, new { name = "Denied", type = "personal" }, csrf: false), Vaults, "post", 400);
        await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Denied" }, csrf: false), Registration, "post", 400);
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().RevokeAsync(_ownerId);
        await Contract(await _owner.Send("GET", $"/api/v1/assets/{asset:D}"), Inspection, "get", 401);
        Assert.That(await AssetCount(), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangesCommittedAfterAuthenticationBeforeRegistrationPreventWrite(bool removeMember)
    {
        var vault = await CreateVault();
        await Sql($"INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ({vault}, {_otherId}, {2})");
        _gate.Enabled = true;
        var pending = _other.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Must not be saved" });
        await _gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            if (removeMember) await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Memberships WHERE VaultId = {vault} AND ActorId = {_otherId}");
            else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Vaults SET Status = {1} WHERE Id = {vault}");
            await transaction.CommitAsync();
        }
        finally { _gate.Release.TrySetResult(); }
        await Contract(await pending, Registration, "post", removeMember ? 404 : 409);
        Assert.That(await AssetCount(), Is.Zero);
    }

    [Test]
    public async Task MalformedAndUnsupportedBodiesUseSafeProblemDetails()
    {
        await Contract(await _owner.Send("POST", Vaults, new StringContent("{", System.Text.Encoding.UTF8, "application/json")), Vaults, "post", 400);
        await Contract(await _owner.Send("POST", Vaults, new { name = "Private sentinel", type = 0 }), Vaults, "post", 400);
        await Contract(await _owner.Send("POST", Vaults, new { name = "Private sentinel" }), Vaults, "post", 400);
        await Contract(await _owner.Send("POST", Vaults, new StringContent("Private sentinel")), Vaults, "post", 415);
        await Contract(await _owner.Send("POST", Vaults, new { name = new string('x', 17000), type = "personal" }), Vaults, "post", 413);
        var vault = await CreateVault();
        var blank = await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = " " }), Registration, "post", 400);
        Assert.That(blank.GetProperty("errors").GetProperty("name")[0].GetString(), Is.EqualTo("blank_name"));
        Assert.That(await AssetCount(), Is.Zero);
    }

    [Test]
    public async Task SafeValidationAndUnexpectedStorageFailureFollowContract()
    {
        var blank = await Contract(await _owner.Send("POST", Vaults, new { name = " ", type = "personal" }), Vaults, "post", 400);
        Assert.That(blank.GetProperty("errors").GetProperty("name")[0].GetString(), Is.EqualTo("blank_name"));
        var invalid = await Contract(await _owner.Send("POST", Vaults, new { name = "Private sentinel", type = "wrong" }), Vaults, "post", 400);
        Assert.That(invalid.GetProperty("errors").GetProperty("type")[0].GetString(), Is.EqualTo("invalid_type"));
        await Contract(await _owner.Send("GET", "/api/v1/assets/not-a-guid"), Inspection, "get", 400);
        await Contract(await _owner.Send("POST", $"{Vaults}/{Guid.Empty:D}/assets", new { name = "Private sentinel" }), Registration, "post", 400);
        await Sql($"CREATE TRIGGER FailVault BEFORE INSERT ON Vaults BEGIN SELECT RAISE(ABORT, 'private test storage failure'); END;");
        var failure = await Contract(await _owner.Send("POST", Vaults, new { name = "Private sentinel", type = "personal" }), Vaults, "post", 500);
        Assert.That(failure.GetRawText(), Does.Not.Contain("private").IgnoreCase.And.Not.Contain(_path));
    }

    [TestCase(0, 204)]
    [TestCase(1, 403)]
    [TestCase(2, 403)]
    [TestCase(3, 403)]
    public async Task ArchiveUsesCurrentOwnerAndRetainsReadableAssetsAfterRestart(int role, int status)
    {
        var vault = await CreateVault();
        var asset = await CreateAsset(vault);
        await Sql($"INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ({vault}, {_otherId}, {role})");
        var route = $"{Vaults}/{vault:D}/archive";
        await Contract(await _other.Send("POST", route), Archival, "post", status);
        await Contract(await _owner.Send("POST", route), Archival, "post", 204);
        await Contract(await _other.Send("POST", route), Archival, "post", status);
        await _app.DisposeAsync();
        await Start();
        _owner.Replace(_app.GetTestClient());
        _other.Replace(_app.GetTestClient());
        await Contract(await _owner.Send("POST", route), Archival, "post", 204);
        await Contract(await _other.Send("GET", $"/api/v1/assets/{asset:D}"), Inspection, "get", 200);
        await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/assets", new { name = "Denied" }), Registration, "post", 409);
        Assert.That(await AssetCount(), Is.EqualTo(1));
    }

    [Test]
    public async Task ArchiveRejectsForgedIdentityBodiesMissingAntiforgeryAndRevokedSessions()
    {
        var vault = await CreateVault();
        var route = $"{Vaults}/{vault:D}/archive";
        using var anonymous = new Browser(_app.GetTestClient());
        await Contract(await anonymous.Send("POST", route), Archival, "post", 401);
        await Contract(await _owner.Send("POST", route, csrf: false), Archival, "post", 400);
        await Contract(await _owner.Send("POST", route, new { actorId = _otherId }), Archival, "post", 400);
        await Contract(await _owner.Send("POST", $"{Vaults}/bad-id/archive"), Archival, "post", 400);
        var denied = await Contract(await _other.Send("POST", route + $"?actorId={_ownerId}", actorHeader: _ownerId), Archival, "post", 404);
        var missing = await Contract(await _other.Send("POST", $"{Vaults}/{Guid.NewGuid():D}/archive"), Archival, "post", 404);
        Assert.That(denied.GetRawText(), Is.EqualTo(missing.GetRawText()));
        using (var scope = _app.Services.CreateScope())
            Assert.That(await scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>().Database.SqlQuery<int>(
                $"SELECT Status AS Value FROM Vaults WHERE Id = {vault}").SingleAsync(), Is.Zero);
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().RevokeAsync(_ownerId);
        await Contract(await _owner.Send("POST", route), Archival, "post", 401);
    }

    [Test]
    public async Task ArchiveRechecksRoleWithoutReloginAndHidesStorageFailure()
    {
        var vault = await CreateVault();
        var route = $"{Vaults}/{vault:D}/archive";
        await Sql($"INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ({vault}, {_otherId}, {0})");
        await Sql($"UPDATE Memberships SET Role = {2} WHERE VaultId = {vault} AND ActorId = {_ownerId}");
        await Contract(await _owner.Send("POST", route), Archival, "post", 403);
        await Sql($"DELETE FROM Memberships WHERE VaultId = {vault} AND ActorId = {_ownerId}");
        await Contract(await _owner.Send("POST", route), Archival, "post", 404);
        await Sql($"CREATE TRIGGER FailArchive BEFORE UPDATE ON Vaults BEGIN SELECT RAISE(ABORT, 'private archive failure'); END;");
        var failure = await Contract(await _other.Send("POST", route), Archival, "post", 500);
        Assert.That(failure.GetRawText(), Does.Not.Contain("private").And.Not.Contain(_path));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ArchiveRejectsPermissionChangeCommittedAfterAuthentication(bool remove)
    {
        var vault = await CreateVault();
        await Sql($"INSERT INTO Memberships (VaultId, ActorId, Role) VALUES ({vault}, {_otherId}, {0})");
        _gate.Enabled = true;
        var pending = _owner.Send("POST", $"{Vaults}/{vault:D}/archive");
        await _gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            if (remove) await Sql($"DELETE FROM Memberships WHERE VaultId = {vault} AND ActorId = {_ownerId}");
            else await Sql($"UPDATE Memberships SET Role = {2} WHERE VaultId = {vault} AND ActorId = {_ownerId}");
        }
        finally { _gate.Release.TrySetResult(); }
        await Contract(await pending, Archival, "post", remove ? 404 : 403);
        using var scope = _app.Services.CreateScope();
        Assert.That(await scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>().Database.SqlQuery<int>(
            $"SELECT Status AS Value FROM Vaults WHERE Id = {vault}").SingleAsync(), Is.Zero);
    }

    private async Task<Guid> EnrollAndLogin(Browser browser, string login)
    {
        AccountCredential credential;
        using (var scope = _app.Services.CreateScope())
            credential = (await scope.ServiceProvider.GetRequiredService<AccountOperations>().IssueAsync(login, false))!;
        using (var scope = _app.Services.CreateScope())
            Assert.That(await scope.ServiceProvider.GetRequiredService<AccountOperations>().RedeemAsync(login, credential.Secret, Password, false), Is.True);
        await browser.Send("GET", "/auth/antiforgery");
        Assert.That((await browser.Send("POST", "/auth/login", new { login, Password })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await browser.Send("GET", "/auth/antiforgery");
        var session = await browser.Send("GET", "/auth/session");
        return (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actorId").GetGuid();
    }

    private async Task<Guid> CreateVault() => (await Contract(await _owner.Send("POST", Vaults,
        new { name = "Fictional Vault", type = "household" }), Vaults, "post", 201)).GetProperty("id").GetGuid();

    private async Task<Guid> CreateAsset(Guid vault) => (await Contract(await _owner.Send("POST", $"{Vaults}/{vault:D}/assets",
        new { name = "Fictional Asset" }), Registration, "post", 201)).GetProperty("id").GetGuid();

    private async Task Sql(FormattableString sql)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>().Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private async Task<long> AssetCount()
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>().Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM Assets").SingleAsync();
    }

    private async Task<JsonElement> Contract(HttpResponseMessage response, string path, string method, int status)
    {
        Assert.That((int)response.StatusCode, Is.EqualTo(status));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        var documented = _contract.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("responses").GetProperty(status.ToString());
        documented = Resolve(documented);
        if (status == 204)
        {
            Assert.That(documented.TryGetProperty("content", out _), Is.False);
            Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
            return default;
        }
        var mediaType = status >= 400 ? "application/problem+json" : "application/json";
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo(mediaType));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        ValidateShape(body, documented.GetProperty("content").GetProperty(mediaType).GetProperty("schema"));
        if (status >= 400)
        {
            Assert.That(body.GetProperty("status").GetInt32(), Is.EqualTo(status));
            Assert.That(body.GetRawText(), Does.Not.Contain("Private sentinel"));
        }
        return body;
    }

    // Checks the shapes actually used by this contract; not a general JSON Schema validator.
    private void ValidateShape(JsonElement value, JsonElement schema)
    {
        schema = Resolve(schema);
        if (schema.TryGetProperty("enum", out var choices))
            Assert.That(choices.EnumerateArray().Select(choice => choice.GetRawText()), Does.Contain(value.GetRawText()));
        switch (schema.GetProperty("type").GetString())
        {
            case "object":
                Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Object));
                if (schema.TryGetProperty("required", out var required))
                    foreach (var property in required.EnumerateArray()) Assert.That(value.TryGetProperty(property.GetString()!, out _), Is.True);
                foreach (var property in value.EnumerateObject())
                {
                    if (schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty(property.Name, out var nested)) ValidateShape(property.Value, nested);
                    else if (schema.TryGetProperty("additionalProperties", out var extra))
                    {
                        Assert.That(extra.ValueKind, Is.Not.EqualTo(JsonValueKind.False), $"Unexpected field {property.Name}");
                        if (extra.ValueKind == JsonValueKind.Object) ValidateShape(property.Value, extra);
                    }
                }
                break;
            case "array":
                Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Array));
                foreach (var item in value.EnumerateArray()) ValidateShape(item, schema.GetProperty("items"));
                break;
            case "string":
                Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.String));
                if (schema.TryGetProperty("pattern", out var pattern)) Assert.That(Regex.IsMatch(value.GetString()!, pattern.GetString()!), Is.True);
                if (schema.TryGetProperty("format", out var format) && format.GetString() == "uuid") Assert.That(value.GetGuid(), Is.Not.EqualTo(Guid.Empty));
                break;
            case "integer": Assert.That(value.TryGetInt32(out _), Is.True); break;
        }
    }

    private JsonElement Resolve(JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference)) return schema;
        var resolved = _contract.RootElement;
        foreach (var segment in reference.GetString()![2..].Split('/')) resolved = resolved.GetProperty(segment);
        return resolved;
    }

    private sealed class Browser(HttpClient client) : IDisposable
    {
        private HttpClient _client = client;
        private readonly CookieContainer _cookies = new();
        private static readonly Uri Origin = new("https://localhost");
        internal void Replace(HttpClient replacement) { _client.Dispose(); _client = replacement; }
        public void Dispose() => _client.Dispose();
        internal async Task<HttpResponseMessage> Send(string method, string path, object? body = null, bool csrf = true, Guid? actorHeader = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(Origin, path));
            if (body is HttpContent content) request.Content = content;
            else if (body is not null) request.Content = JsonContent.Create(body);
            request.Headers.TryAddWithoutValidation("Cookie", _cookies.GetCookieHeader(Origin));
            var token = _cookies.GetCookies(Origin)["XSRF-TOKEN"]?.Value;
            if (csrf && token is not null) request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", Uri.UnescapeDataString(token));
            if (actorHeader is not null) request.Headers.TryAddWithoutValidation("X-Actor-Id", actorHeader.Value.ToString());
            var response = await _client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var headers))
                foreach (var cookie in headers) _cookies.SetCookies(Origin, cookie);
            return response;
        }
    }

    private sealed class RegistrationGate
    {
        internal bool Enabled;
        internal TaskCompletionSource Entered = null!;
        internal TaskCompletionSource Release = null!;
        internal void Reset()
        {
            Enabled = false;
            Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class GatedRegistrationStore(IAssetRegistrationStore inner, RegistrationGate gate) : IAssetRegistrationStore
    {
        public async Task<AssetRegistrationOutcome> RegisterAsync(Asset asset, Guid actorId, CancellationToken cancellationToken)
        {
            if (gate.Enabled)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return await inner.RegisterAsync(asset, actorId, cancellationToken);
        }
    }

    private sealed class GatedArchiveStore(IVaultArchiveStore inner, RegistrationGate gate) : IVaultArchiveStore
    {
        public async Task<ArchiveVaultOutcome> ArchiveAsync(Guid vaultId, Guid actorId, CancellationToken cancellationToken)
        {
            if (gate.Enabled)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return await inner.ArchiveAsync(vaultId, actorId, cancellationToken);
        }
    }
}
