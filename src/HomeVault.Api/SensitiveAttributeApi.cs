using System.Text.Json.Serialization;
using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;

namespace HomeVault.Api;

internal static class SensitiveAttributeApi
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assets/{assetId}/sensitive-attributes").RequireAuthorization();
        group.MapPost("/add", async (string assetId, AddSensitiveAttributeInput input, SensitiveAttributeUseCases useCase, CancellationToken token) =>
        {
            if (!Identity(assetId, out var asset)) return ApiProblems.Result(400, "invalid_identity");
            var result = await useCase.AddAsync(asset, input.Name, input.Value, input.Sensitivity == "sensitive" ? AttributeSensitivity.Sensitive : (AttributeSensitivity)(-1), token);
            return result.Outcome == SensitiveAttributeOutcome.Succeeded ? Results.Json(new { id = result.Id }, statusCode: 201) : Failure(result.Outcome);
        });
        group.MapGet("", async (string assetId, SensitiveAttributeUseCases useCase, CancellationToken token) =>
        {
            if (!Identity(assetId, out var asset)) return ApiProblems.Result(400, "invalid_identity");
            var result = await useCase.ListAsync(asset, token);
            return result.Outcome == SensitiveAttributeOutcome.Succeeded ? Results.Ok(new { attributes = result.Metadata!.Select(row => new { id = row.Id, name = row.Name, sensitivity = "sensitive" }) }) : Failure(result.Outcome);
        });
        group.MapPost("/{attributeId}/change", async (string assetId, string attributeId, ChangeSensitiveAttributeInput input, SensitiveAttributeUseCases useCase, CancellationToken token) =>
        {
            if (!Identity(assetId, out var asset) || !Identity(attributeId, out var attribute)) return ApiProblems.Result(400, "invalid_identity");
            return Failure((await useCase.ChangeAsync(asset, attribute, input.Value, token)).Outcome);
        });
        group.MapPost("/{attributeId}/remove", async (string assetId, string attributeId, HttpContext context, SensitiveAttributeUseCases useCase, CancellationToken token) =>
        {
            if (await context.Request.Body.ReadAsync(new byte[1], token) != 0) return ApiProblems.Result(400);
            if (!Identity(assetId, out var asset) || !Identity(attributeId, out var attribute)) return ApiProblems.Result(400, "invalid_identity");
            return Failure((await useCase.RemoveAsync(asset, attribute, token)).Outcome);
        });
        group.MapPost("/{attributeId}/read", async (string assetId, string attributeId, HttpContext context, SensitiveAttributeUseCases useCase, CancellationToken token) =>
        {
            if (await context.Request.Body.ReadAsync(new byte[1], token) != 0) return ApiProblems.Result(400);
            if (!Identity(assetId, out var asset) || !Identity(attributeId, out var attribute)) return ApiProblems.Result(400, "invalid_identity");
            var result = await useCase.ReadAsync(asset, attribute, token);
            return result.Outcome == SensitiveAttributeOutcome.Succeeded ? Results.Ok(new { value = result.ReadValue() }) : Failure(result.Outcome);
        });
    }
    private static bool Identity(string text, out Guid id) => Guid.TryParseExact(text, "D", out id) && id != Guid.Empty;
    private static IResult Failure(SensitiveAttributeOutcome outcome) => outcome switch
    {
        SensitiveAttributeOutcome.Succeeded => Results.NoContent(),
        SensitiveAttributeOutcome.Unauthenticated => ApiProblems.Result(401),
        SensitiveAttributeOutcome.InvalidIdentity => ApiProblems.Result(400, "invalid_identity"),
        SensitiveAttributeOutcome.Unavailable => ApiProblems.Result(404),
        SensitiveAttributeOutcome.Forbidden => ApiProblems.Result(403),
        SensitiveAttributeOutcome.Archived => ApiProblems.Result(409, "vault_archived"),
        SensitiveAttributeOutcome.InvalidName => ApiProblems.Result(400, "invalid_name", "name"),
        SensitiveAttributeOutcome.InvalidValue => ApiProblems.Result(400, "invalid_value", "value"),
        SensitiveAttributeOutcome.UnsupportedSensitivity => ApiProblems.Result(400, "unsupported_sensitivity", "sensitivity"),
        SensitiveAttributeOutcome.DuplicateName => ApiProblems.Result(409, "attribute_exists"),
        SensitiveAttributeOutcome.SensitiveUnavailable => ApiProblems.Result(503, "sensitive_unavailable"),
        _ => ApiProblems.Result(500)
    };
}

/// <summary>Explicit Sensitive addition; no caller identity or storage ID is accepted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AddSensitiveAttributeInput
{
    /// <summary>Visible label; never place secrets here.</summary>
    public string? Name { get; set; }
    /// <summary>Private text, preserved exactly and never logged.</summary>
    public string? Value { get; set; }
    /// <summary>Required literal sensitive.</summary>
    public string? Sensitivity { get; set; }
}

/// <summary>Private text replacement without renaming, identity changes or reclassification.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeSensitiveAttributeInput
{
    /// <summary>Nonblank private text; never log or include in diagnostics.</summary>
    public string? Value { get; set; }
}
