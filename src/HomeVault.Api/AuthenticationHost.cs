using System.Security.Claims;
using System.Threading.RateLimiting;
using HomeVault.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace HomeVault.Api;

/// <summary>Configures the reviewed cookie API without public signup or fictional actors.</summary>
public static class AuthenticationHost
{
    private const string Scheme = "HomeVault";
    private const string Stamp = "homevault:stamp";

    /// <summary>Registers framework authentication, antiforgery, rate limits and account services.</summary>
    /// <param name="services">Composition-root services.</param>
    /// <param name="databasePath">Existing migrated database.</param>
    /// <param name="keys">Validated persistent key provider owned by the host.</param>
    public static void Configure(IServiceCollection services, string databasePath, IDataProtectionProvider keys)
    {
        services.AddHomeVaultAccounts(databasePath, keys);
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "__Host-HomeVault";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = async context =>
            {
                var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var stamp = context.Principal?.FindFirstValue(Stamp);
                if (!Guid.TryParse(id, out var actor) ||
                    !await context.HttpContext.RequestServices.GetRequiredService<AccountOperations>().ValidateSessionAsync(actor, stamp))
                    context.RejectPrincipal();
            };
        });
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "__Host-HomeVault-Antiforgery";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            // One loopback host: a global bound cannot be bypassed by cycling login identifiers.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter("local", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("credentials", _ => RateLimitPartition.GetFixedWindowLimiter("credentials", _ =>
                new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    /// <summary>Maps authentication endpoints and fail-closed middleware; the host supplies HTTPS and allowed-host settings.</summary>
    /// <param name="app">Built application; call once before running.</param>
    public static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                if (!context.Request.IsHttps) { context.Response.StatusCode = 400; return; }
                var size = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (size is { IsReadOnly: false }) size.MaxRequestBodySize = 16 * 1024;
                if (context.Request.ContentLength > 16 * 1024) { context.Response.StatusCode = 413; return; }
                if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
                {
                    // Bound chunked requests too, including hosts without Kestrel's size feature.
                    var bytes = new byte[16 * 1024 + 1];
                    var count = await context.Request.Body.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, context.RequestAborted);
                    if (count == bytes.Length) { context.Response.StatusCode = 413; return; }
                    using var body = new MemoryStream(bytes, 0, count, writable: false);
                    context.Request.Body = body;
                    await next(context);
                }
                else await next(context);
            }
            catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; }
            catch (BadHttpRequestException) { context.Response.StatusCode = 400; }
            catch { if (!context.Response.HasStarted) { context.Response.Clear(); context.Response.StatusCode = 503; } else context.Abort(); }
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
                await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            await next(context);
        });
        app.MapGet("/auth/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var token = antiforgery.GetAndStoreTokens(context).RequestToken!;
            context.Response.Cookies.Append("XSRF-TOKEN", token, new CookieOptions
            { Secure = true, HttpOnly = false, SameSite = SameSiteMode.Strict, Path = "/" });
            return Results.NoContent();
        }).AllowAnonymous();
        app.MapPost("/auth/login", async (LoginInput input, HttpContext context, AccountOperations accounts) =>
        {
            var user = await accounts.LoginAsync(input.Login, input.Password);
            if (user is null) return Results.Unauthorized();
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(Stamp, user.SecurityStamp!)
            }, Scheme);
            await context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
            ClearAntiforgery(context);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("credentials");
        app.MapPost("/auth/invitations/redeem", async (RedemptionInput input, AccountOperations accounts) =>
            await accounts.RedeemAsync(input.Login, input.Secret, input.Password, false) ? Results.NoContent() : Results.BadRequest())
            .AllowAnonymous().RequireRateLimiting("credentials");
        app.MapPost("/auth/recovery/redeem", async (RedemptionInput input, HttpContext context, AccountOperations accounts) =>
        {
            if (!await accounts.RedeemAsync(input.Login, input.Secret, input.Password, true)) return Results.BadRequest();
            await context.SignOutAsync(Scheme);
            ClearAntiforgery(context);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("credentials");
        app.MapGet("/auth/session", (HttpContext context) => Results.Ok(new { ActorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) }));
        app.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(Scheme);
            ClearAntiforgery(context);
            return Results.NoContent();
        });
        app.MapPost("/auth/sign-out-all", async (HttpContext context, AccountOperations accounts) =>
        {
            if (!await accounts.RevokeAsync(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!))) return Results.Unauthorized();
            await context.SignOutAsync(Scheme);
            ClearAntiforgery(context);
            return Results.NoContent();
        });
    }

    private static void ClearAntiforgery(HttpContext context)
    {
        var options = new CookieOptions { Secure = true, Path = "/", SameSite = SameSiteMode.Strict };
        context.Response.Cookies.Delete("XSRF-TOKEN", options);
        options.HttpOnly = true;
        context.Response.Cookies.Delete("__Host-HomeVault-Antiforgery", options);
    }
}

/// <summary>Bounded login body; never log or echo submitted fields.</summary>
public sealed class LoginInput
{
    /// <summary>Submitted email/login.</summary>
    public string Login { get; set; } = string.Empty;
    /// <summary>Submitted password; sensitive.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>One-use enrollment/reset body. All fields are excluded from diagnostics.</summary>
public sealed class RedemptionInput
{
    /// <summary>Recipient login.</summary>
    public string Login { get; set; } = string.Empty;
    /// <summary>Exported bearer credential.</summary>
    public string Secret { get; set; } = string.Empty;
    /// <summary>Recipient-chosen password.</summary>
    public string Password { get; set; } = string.Empty;
}
