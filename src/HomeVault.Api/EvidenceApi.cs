using System.Text.Json.Serialization;
using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;

namespace HomeVault.Api;

internal static class EvidenceApi
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assets/{assetId}/evidence").RequireAuthorization();
        group.MapPost("", async (string assetId, AddEvidenceInput input, EvidenceUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(assetId, out var asset) || !Identity(input.Id, out var id)) return ApiProblems.Result(400, "invalid_identity");
            var kind = input.Kind switch { "url" => EvidenceKind.Url, "note" => EvidenceKind.Note, _ => (EvidenceKind)(-1) };
            var outcome = await useCases.AddAsync(asset, id, input.Label, kind, input.Content, token);
            return outcome == EvidenceOutcome.Succeeded
                ? Results.Created($"/api/v1/assets/{asset:D}/evidence/{id:D}/content", new { id, label = input.Label, kind = input.Kind })
                : Failure(outcome);
        });
        group.MapDelete("/{evidenceId}", async (string assetId, string evidenceId, HttpContext context, EvidenceUseCases useCases, CancellationToken token) =>
        {
            if (await context.Request.Body.ReadAsync(new byte[1], token) != 0) return ApiProblems.Result(400);
            if (!Identity(assetId, out var asset) || !Identity(evidenceId, out var id)) return ApiProblems.Result(400, "invalid_identity");
            var outcome = await useCases.RemoveAsync(asset, id, token);
            return outcome == EvidenceOutcome.Succeeded ? Results.NoContent() : Failure(outcome);
        });
        group.MapGet("", async (string assetId, EvidenceUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(assetId, out var asset)) return ApiProblems.Result(400, "invalid_identity");
            var entries = await useCases.ListAsync(asset, token);
            return entries is null ? ApiProblems.Result(404) : Results.Ok(new
            {
                evidence = entries.Select(entry => new { id = entry.Id, label = entry.Label, kind = entry.Kind == EvidenceKind.Url ? "url" : "note" }).ToArray()
            });
        });
        group.MapGet("/{evidenceId}/content", async (string assetId, string evidenceId, EvidenceUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(assetId, out var asset) || !Identity(evidenceId, out var id)) return ApiProblems.Result(400, "invalid_identity");
            var entry = await useCases.ReadContentAsync(asset, id, token);
            return entry is null ? ApiProblems.Result(404) : Results.Ok(new { content = entry.ReadContent() });
        });
    }
    private static bool Identity(string? text, out Guid id) => Guid.TryParseExact(text, "D", out id) && id != Guid.Empty;
    private static IResult Failure(EvidenceOutcome outcome) => outcome switch
    {
        EvidenceOutcome.Unauthenticated => ApiProblems.Result(401),
        EvidenceOutcome.InvalidIdentity => ApiProblems.Result(400, "invalid_identity"),
        EvidenceOutcome.Unavailable => ApiProblems.Result(404),
        EvidenceOutcome.Forbidden => ApiProblems.Result(403),
        EvidenceOutcome.Archived => ApiProblems.Result(409, "vault_archived"),
        EvidenceOutcome.BlankLabel => ApiProblems.Result(400, "blank_label", "label"),
        EvidenceOutcome.InvalidKind => ApiProblems.Result(400, "invalid_kind", "kind"),
        EvidenceOutcome.BlankContent => ApiProblems.Result(400, "blank_content", "content"),
        EvidenceOutcome.InvalidUrl => ApiProblems.Result(400, "invalid_url", "content"),
        EvidenceOutcome.DuplicateIdentity => ApiProblems.Result(409, "evidence_exists"),
        _ => ApiProblems.Result(500)
    };
}

/// <summary>Caller-identified URL/Note Evidence input; no requester or Vault identity is accepted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AddEvidenceInput
{
    /// <summary>Nonempty D-format Guid, unique within the owning Asset.</summary>
    public string? Id { get; set; }
    /// <summary>Nonblank descriptive label, preserved exactly; must not contain secrets.</summary>
    public string? Label { get; set; }
    /// <summary>Required lowercase url or note.</summary>
    public string? Kind { get; set; }
    /// <summary>Original nonblank text or validated HTTP/HTTPS reference; stored as plaintext, never fetched or logged.</summary>
    public string? Content { get; set; }
}
