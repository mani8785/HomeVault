using System.Text.Json.Serialization;
using HomeVault.Application.Relationships;
using HomeVault.Domain.Relationships;

namespace HomeVault.Api;

internal static class RelationshipApi
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/vaults/{vaultId}/relationships").RequireAuthorization();
        group.MapPost("", async (string vaultId, CreateRelationshipInput input, RelationshipUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Identity(input.Id, out var id) ||
                !Identity(input.SourceAssetId, out var source) || !Identity(input.TargetAssetId, out var target)) return ApiProblems.Result(400, "invalid_identity");
            var kind = input.Kind == "covers" ? RelationshipKind.Covers : (RelationshipKind)(-1);
            var outcome = await useCases.CreateAsync(vault, id, source, target, kind, token);
            return outcome == RelationshipOutcome.Succeeded
                ? Results.Created($"/api/v1/vaults/{vault:D}/relationships/{id:D}", Output(new RelationshipMetadata(id, vault, source, target, kind, RelationshipStatus.Active)))
                : Failure(outcome);
        });
        group.MapGet("/{relationshipId}", async (string vaultId, string relationshipId, RelationshipUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Identity(relationshipId, out var id)) return ApiProblems.Result(400, "invalid_identity");
            var root = await useCases.FindAsync(vault, id, token);
            return root is null ? ApiProblems.Result(404) : Results.Ok(Output(root));
        });
        group.MapDelete("/{relationshipId}", async (string vaultId, string relationshipId, HttpContext context, RelationshipUseCases useCases, CancellationToken token) =>
        {
            if (await context.Request.Body.ReadAsync(new byte[1], token) != 0) return ApiProblems.Result(400);
            if (!Identity(vaultId, out var vault) || !Identity(relationshipId, out var id)) return ApiProblems.Result(400, "invalid_identity");
            var outcome = await useCases.RemoveAsync(vault, id, token);
            return outcome == RelationshipOutcome.Succeeded ? Results.NoContent() : Failure(outcome);
        });
    }
    private static object Output(RelationshipMetadata root) => new
    {
        id = root.Id,
        vaultId = root.VaultId,
        sourceAssetId = root.SourceAssetId,
        targetAssetId = root.TargetAssetId,
        kind = "covers",
        status = root.Status == RelationshipStatus.Active ? "active" : "removed"
    };
    private static bool Identity(string? text, out Guid id) => Guid.TryParseExact(text, "D", out id) && id != Guid.Empty;
    private static IResult Failure(RelationshipOutcome outcome) => outcome switch
    {
        RelationshipOutcome.Unauthenticated => ApiProblems.Result(401),
        RelationshipOutcome.InvalidIdentity => ApiProblems.Result(400, "invalid_identity"),
        RelationshipOutcome.Unavailable => ApiProblems.Result(404),
        RelationshipOutcome.Forbidden => ApiProblems.Result(403),
        RelationshipOutcome.Archived => ApiProblems.Result(409, "vault_archived"),
        RelationshipOutcome.InvalidKind => ApiProblems.Result(400, "invalid_kind", "kind"),
        RelationshipOutcome.SelfReference => ApiProblems.Result(400, "self_reference"),
        RelationshipOutcome.IdentityConflict => ApiProblems.Result(409, "identity_conflict"),
        RelationshipOutcome.DuplicateRelationship => ApiProblems.Result(409, "relationship_exists"),
        _ => ApiProblems.Result(500)
    };
}

/// <summary>Caller-identified directed link input; route Vault is verified against actual stored endpoints.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateRelationshipInput
{
    /// <summary>Nonempty D-format root Guid, never reusable after removal.</summary>
    public string? Id { get; set; }
    /// <summary>Nonempty D-format source Asset Guid.</summary>
    public string? SourceAssetId { get; set; }
    /// <summary>Nonempty D-format target Asset Guid, distinct from source.</summary>
    public string? TargetAssetId { get; set; }
    /// <summary>Required literal covers; endpoint categories remain caller responsibilities.</summary>
    public string? Kind { get; set; }
}
