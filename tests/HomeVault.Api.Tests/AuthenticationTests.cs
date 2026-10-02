using System.Net;
using System.Net.Http.Json;
using HomeVault.Infrastructure.Identity;
using HomeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace HomeVault.Api.Tests;

[TestFixture]
public sealed class AuthenticationTests
{
    private string _directory = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly CookieContainer _cookies = new();
    private readonly Uri _origin = new("https://localhost");
    private IDataProtectionProvider _keys = null!;
    private SessionClock _sessionClock = null!;
    private const string Login = "fictional@example.invalid";
    private const string Password = "fictional sufficiently long password";

    [SetUp]
    public async Task SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "HomeVaultHttp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        await new SqliteDatabase(Path.Combine(_directory, "accounts.db")).MigrateAsync();
        _keys = new EphemeralDataProtectionProvider();
        _sessionClock = new SessionClock();
        await Start();
    }

    private async Task Start()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        AuthenticationHost.Configure(builder.Services, Path.Combine(_directory, "accounts.db"), _keys);
        builder.Services.Configure<CookieAuthenticationOptions>("HomeVault", options => options.TimeProvider = _sessionClock);
        _app = builder.Build();
        AuthenticationHost.Map(_app);
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = _origin;
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        foreach (Cookie cookie in _cookies.GetAllCookies()) cookie.Expired = true;
        Directory.Delete(_directory, true);
    }

    [Test]
    [Platform("Win")]
    public async Task CookieSurvivesReopeningActualWindowsProtectedRing()
    {
        var path = Path.Combine(_directory, "session-keys");
        WindowsSessionKeys.Initialize(path);
        _client.Dispose();
        await _app.DisposeAsync();
        using var first = WindowsSessionKeys.Open(path);
        _keys = first;
        await Start();
        await Enroll();
        await Csrf();
        Assert.That((await Send("POST", "/auth/login", new { Login, Password })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _client.Dispose();
        await _app.DisposeAsync();
        first.Dispose();
        using var reopened = WindowsSessionKeys.Open(path);
        _keys = reopened;
        await Start();
        Assert.That((await Send("GET", "/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SessionExpiresAbsolutelyAndAuthenticatedMutationsRequireFreshAntiforgery()
    {
        await Enroll();
        await Csrf();
        var anonymousToken = _cookies.GetCookies(_origin)["XSRF-TOKEN"]!.Value;
        var anonymousCookie = _cookies.GetCookies(_origin)["__Host-HomeVault-Antiforgery"]!.Value;
        await Send("POST", "/auth/login", new { Login, Password });
        _cookies.Add(_origin, new Cookie("XSRF-TOKEN", anonymousToken, "/") { Secure = true });
        _cookies.Add(_origin, new Cookie("__Host-HomeVault-Antiforgery", anonymousCookie, "/") { Secure = true });
        Assert.That((await Send("POST", "/auth/logout", new { })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Send("POST", "/auth/sign-out-all", new { }, csrf: false)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        _sessionClock.Now = _sessionClock.Now.AddHours(7);
        var stillActive = await Send("GET", "/auth/session");
        Assert.That(stillActive.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(stillActive.Headers.Contains("Set-Cookie"), Is.False, "Reads must not slide the expiry.");
        _sessionClock.Now = _sessionClock.Now.AddHours(2);
        Assert.That((await Send("GET", "/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task CookieFlagsAntiforgeryRestartLogoutAndSignOutAll()
    {
        await Enroll();
        Assert.That((await Send("GET", "/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await Send("POST", "/auth/login", new { Login, Password }, csrf: false)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await Csrf();
        var login = await Send("POST", "/auth/login", new { Login, Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var setCookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-HomeVault="));
        Assert.That(setCookie.ToLowerInvariant(), Does.Contain("secure").And.Contain("httponly").And.Contain("samesite=lax").And.Contain("path=/"));
        Assert.That(setCookie.ToLowerInvariant(), Does.Not.Contain("domain=").And.Not.Contain("expires="));
        var stolen = _cookies.GetCookies(_origin)["__Host-HomeVault"]!.Value;
        Assert.That((await Send("GET", "/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        _client.Dispose();
        await _app.DisposeAsync();
        await Start();
        Assert.That((await Send("GET", "/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await Csrf();
        Assert.That((await Send("POST", "/auth/logout", new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await Send("GET", "/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        _cookies.Add(_origin, new Cookie("__Host-HomeVault", stolen, "/") { Secure = true });
        Assert.That((await Send("GET", "/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK), "Logout alone does not revoke a stolen copy.");
        await Csrf();
        Assert.That((await Send("POST", "/auth/sign-out-all", new { })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _cookies.Add(_origin, new Cookie("__Host-HomeVault", stolen, "/") { Secure = true });
        Assert.That((await Send("GET", "/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task RecoveryAndDisableInvalidateCookieAndNoPublicSignupExists()
    {
        await Enroll();
        await Csrf();
        await Send("POST", "/auth/login", new { Login, Password });
        var recovery = await Operate(accounts => accounts.IssueAsync(Login, true));
        Assert.That((await Send("GET", "/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        await Csrf();
        var body = new { Login, recovery!.Secret, Password = "changed fictional password" };
        Assert.That((await Send("POST", "/auth/recovery/redeem", body)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await Csrf();
        Assert.That((await Send("POST", "/auth/recovery/redeem", body)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Send("POST", "/auth/login", new { Login, body.Password })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var user = await Operate(accounts => accounts.LoginAsync(Login, body.Password));
        await Operate(accounts => accounts.RevokeAsync(user!.Id, true));
        Assert.That((await Send("GET", "/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await Send("POST", "/register", new { Login, Password })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("/auth/login")]
    [TestCase("/auth/invitations/redeem")]
    [TestCase("/auth/recovery/redeem")]
    public async Task EveryAnonymousMutationRequiresAntiforgery(string route)
    {
        Assert.That((await Send("POST", route, new { Login, Password, Secret = "fictional" }, csrf: false)).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task LoginFailuresAreGenericAndCredentialEndpointsAreRateLimited()
    {
        await Enroll();
        await Csrf();
        var known = await Send("POST", "/auth/login", new { Login, Password = "wrong fictional password" });
        var unknown = await Send("POST", "/auth/login", new { Login = "unknown@example.invalid", Password });
        Assert.That(known.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(unknown.StatusCode, Is.EqualTo(known.StatusCode));
        Assert.That(await known.Content.ReadAsStringAsync(), Is.EqualTo(await unknown.Content.ReadAsStringAsync()));
        HttpResponseMessage last = known;
        for (var index = 0; index < 10; index++) last = await Send("POST", "/auth/login", new { Login, Password = "wrong fictional password" });
        Assert.That(last.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(await last.Content.ReadAsStringAsync(), Does.Not.Contain(Login).And.Not.Contain(Password));
    }

    [Test]
    public async Task OversizedBodiesAndHttpAreRejected()
    {
        await Csrf();
        Assert.That((await Send("POST", "/auth/login", new { Login, Password = new string('x', 17000) })).StatusCode,
            Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        using var response = await _client.GetAsync("http://localhost/auth/antiforgery");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UnavailableIdentityStorageFailsClosed()
    {
        await Enroll();
        await Csrf();
        await Send("POST", "/auth/login", new { Login, Password });
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HomeVaultDbContext>().Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers RENAME TO OfflineUsers");
        var response = await Send("GET", "/auth/session");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
    }

    private async Task Enroll()
    {
        var credential = (await Operate(accounts => accounts.IssueAsync(Login, false)))!;
        await Csrf();
        Assert.That((await Send("POST", "/auth/invitations/redeem", new { Login, credential.Secret, Password })).StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent));
    }

    private async Task<T> Operate<T>(Func<AccountOperations, Task<T>> operation)
    {
        using var scope = _app.Services.CreateScope();
        return await operation(scope.ServiceProvider.GetRequiredService<AccountOperations>());
    }

    private async Task Csrf() => Assert.That((await Send("GET", "/auth/antiforgery")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

    private async Task<HttpResponseMessage> Send(string method, string path, object? body = null, bool csrf = true)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.TryAddWithoutValidation("Cookie", _cookies.GetCookieHeader(_origin));
        var token = _cookies.GetCookies(_origin)["XSRF-TOKEN"]?.Value;
        if (csrf && token is not null) request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", Uri.UnescapeDataString(token));
        var response = await _client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var headers))
            foreach (var cookie in headers) _cookies.SetCookies(_origin, cookie);
        return response;
    }

    private sealed class SessionClock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
