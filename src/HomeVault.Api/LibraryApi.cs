using HomeVault.Application.Library;

namespace HomeVault.Api;

internal static class LibraryApi
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization();
        group.MapGet("/vaults", async (HttpRequest request, BrowseLibrary browse, CancellationToken token) =>
        {
            if (!Options(request, out var query)) return ApiProblems.Result(400);
            var page = await browse.VaultsAsync(query, token);
            return page is null ? ApiProblems.Result(404) : Results.Ok(new
            {
                items = page.Items.Select(row => new { row.Id, row.Name, type = row.Type switch { 0 => "personal", 1 => "household", _ => "organization" }, status = row.Status == 0 ? "active" : "archived", role = Role(row.Role) }),
                page.HasMore
            });
        });
        group.MapGet("/vaults/{vaultId}/assets", async (string vaultId, HttpRequest request, BrowseLibrary browse, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Options(request, out var query)) return ApiProblems.Result(400);
            var page = await browse.AssetsAsync(vault, query, token);
            return page is null ? ApiProblems.Result(404) : Results.Ok(page);
        });
        group.MapGet("/vaults/{vaultId}/members", async (string vaultId, HttpRequest request, BrowseLibrary browse, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Options(request, out var query)) return ApiProblems.Result(400);
            var page = await browse.MembersAsync(vault, query, token);
            return page is null ? ApiProblems.Result(404) : Results.Ok(new { items = page.Items.Select(row => new { row.ActorId, role = Role(row.Role) }), page.HasMore });
        });
        group.MapGet("/vaults/{vaultId}/relationships", async (string vaultId, HttpRequest request, BrowseLibrary browse, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Options(request, out var query) || !Asset(request, out var asset)) return ApiProblems.Result(400);
            var page = await browse.RelationshipsAsync(vault, asset, query, token);
            return page is null ? ApiProblems.Result(404) : Results.Ok(new
            {
                items = page.Items.Select(row => new { row.Id, vaultId = vault, row.SourceAssetId, row.TargetAssetId, kind = "covers", status = row.Status == 0 ? "active" : "removed" }),
                page.HasMore
            });
        });
        group.MapGet("/vaults/{vaultId}/reminders", async (string vaultId, HttpRequest request, BrowseLibrary browse, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Options(request, out var query) || !Asset(request, out var asset)) return ApiProblems.Result(400);
            var page = await browse.RemindersAsync(vault, asset, query, token);
            return page is null ? ApiProblems.Result(404) : Results.Ok(new
            {
                items = page.Items.Select(row => new { row.Id, vaultId = vault, row.AssetId, dueAt = new DateTime(row.DueAtUtcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture), status = row.Status switch { 0 => "pending", 1 => "completed", _ => "cancelled" } }),
                page.HasMore
            });
        });
    }

    private static bool Identity(string? value, out Guid id) => Guid.TryParseExact(value, "D", out id) && id != Guid.Empty;
    private static string Role(int role) => role switch { 0 => "owner", 1 => "administrator", 2 => "editor", _ => "viewer" };
    private static bool Asset(HttpRequest request, out Guid? asset)
    {
        asset = null;
        if (!request.Query.ContainsKey("assetId")) return true;
        if (!Identity(request.Query["assetId"], out var id)) return false;
        asset = id; return true;
    }
    private static bool Options(HttpRequest request, out LibraryQuery query)
    {
        query = new LibraryQuery();
        var offset = 0; var limit = 50;
        if (request.Query.ContainsKey("offset") && !int.TryParse(request.Query["offset"], out offset)) return false;
        if (request.Query.ContainsKey("limit") && !int.TryParse(request.Query["limit"], out limit)) return false;
        query = new LibraryQuery(offset, limit, request.Query["search"].ToString());
        return query.IsValid;
    }
}
