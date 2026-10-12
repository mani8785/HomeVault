using System.Text.Json.Serialization;
using HomeVault.Application.Records;

namespace HomeVault.Api;

internal static class NameApi
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization();
        group.MapPut("/vaults/{vaultId}/name", async (string vaultId, RenameInput input, RenameRecords useCase, CancellationToken token) =>
            Identity(vaultId, out var id) ? Result(await useCase.VaultAsync(id, input.Name, input.ExpectedName, token)) : ApiProblems.Result(400, "invalid_identity", "vaultId"));
        group.MapPut("/assets/{assetId}/name", async (string assetId, RenameInput input, RenameRecords useCase, CancellationToken token) =>
            Identity(assetId, out var id) ? Result(await useCase.AssetAsync(id, input.Name, input.ExpectedName, token)) : ApiProblems.Result(400, "invalid_identity", "assetId"));
    }

    private static bool Identity(string text, out Guid id) => Guid.TryParseExact(text, "D", out id) && id != Guid.Empty;
    private static IResult Result(RenameOutcome outcome) => outcome switch
    {
        RenameOutcome.Succeeded => Results.NoContent(),
        RenameOutcome.Unauthenticated => ApiProblems.Result(401),
        RenameOutcome.InvalidIdentity => ApiProblems.Result(400, "invalid_identity"),
        RenameOutcome.Unavailable => ApiProblems.Result(404),
        RenameOutcome.Forbidden => ApiProblems.Result(403),
        RenameOutcome.Archived => ApiProblems.Result(409, "vault_archived"),
        RenameOutcome.InvalidName => ApiProblems.Result(400, "blank_name", "name"),
        RenameOutcome.InvalidExpectedName => ApiProblems.Result(400, "blank_name", "expectedName"),
        RenameOutcome.Conflict => ApiProblems.Result(409, "name_conflict"),
        _ => ApiProblems.Result(500)
    };
}

/// <summary>Replaces a name only when it still equals the exact last-read name; contains no requester identity.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RenameInput
{
    /// <summary>Gets or sets the nonblank replacement name, preserved exactly and never logged.</summary>
    public string? Name { get; set; }
    /// <summary>Gets or sets the exact nonblank last-read name for ordinal comparison.</summary>
    public string? ExpectedName { get; set; }
}
