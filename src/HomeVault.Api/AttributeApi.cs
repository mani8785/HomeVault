using System.Text.Json.Serialization;
using HomeVault.Application.Assets;
using HomeVault.Domain.Assets;

namespace HomeVault.Api;

internal static class AttributeApi
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assets/{assetId}/attributes").RequireAuthorization();
        group.MapPost("/add", async (string assetId, AddAttributeInput input, OrdinaryAttributeUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(assetId, out var id)) return ApiProblems.Result(400, "invalid_identity", "assetId");
            return Result(await useCases.AddAsync(id, input.Name, input.Value,
                input.Sensitivity == "ordinary" ? AttributeSensitivity.Ordinary : (AttributeSensitivity)(-1), token));
        });
        group.MapPost("/change", async (string assetId, ChangeAttributeInput input, OrdinaryAttributeUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(assetId, out var id)) return ApiProblems.Result(400, "invalid_identity", "assetId");
            return Result(await useCases.ChangeAsync(id, input.Name, input.Value, token));
        });
        group.MapPost("/remove", async (string assetId, RemoveAttributeInput input, OrdinaryAttributeUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(assetId, out var id)) return ApiProblems.Result(400, "invalid_identity", "assetId");
            return Result(await useCases.RemoveAsync(id, input.Name, token));
        });
        group.MapGet("", async (string assetId, OrdinaryAttributeUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(assetId, out var id)) return ApiProblems.Result(400, "invalid_identity", "assetId");
            var attributes = await useCases.ListAsync(id, token);
            return attributes is null ? ApiProblems.Result(404) : Results.Ok(new
            {
                attributes = attributes.Select(entry => new { name = entry.Name, value = entry.Value, sensitivity = "ordinary" }).ToArray()
            });
        });
    }

    private static bool Identity(string text, out Guid id) => Guid.TryParseExact(text, "D", out id) && id != Guid.Empty;
    private static IResult Result(AttributeOutcome outcome) => outcome switch
    {
        AttributeOutcome.Succeeded => Results.NoContent(),
        AttributeOutcome.Unauthenticated => ApiProblems.Result(401),
        AttributeOutcome.InvalidIdentity => ApiProblems.Result(400, "invalid_identity"),
        AttributeOutcome.Unavailable => ApiProblems.Result(404),
        AttributeOutcome.Forbidden => ApiProblems.Result(403),
        AttributeOutcome.Archived => ApiProblems.Result(409, "vault_archived"),
        AttributeOutcome.InvalidName => ApiProblems.Result(400, "invalid_name", "name"),
        AttributeOutcome.InvalidValue => ApiProblems.Result(400, "invalid_value", "value"),
        AttributeOutcome.UnsupportedSensitivity => ApiProblems.Result(400, "unsupported_sensitivity", "sensitivity"),
        AttributeOutcome.DuplicateName => ApiProblems.Result(409, "attribute_exists"),
        _ => ApiProblems.Result(500)
    };
}

/// <summary>Explicitly classified ordinary attribute addition; requester identity is never accepted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AddAttributeInput
{
    /// <summary>Nonblank name, trimmed and compared ignoring ordinal case.</summary>
    public string? Name { get; set; }
    /// <summary>Nonblank ordinary text, preserved exactly; never logged.</summary>
    public string? Value { get; set; }
    /// <summary>Required literal ordinary; Sensitive persistence is unsupported.</summary>
    public string? Sensitivity { get; set; }
}

/// <summary>Replacement of one ordinary value without renaming or reclassification.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeAttributeInput
{
    /// <summary>Nonblank lookup name, trimmed and compared ignoring ordinal case.</summary>
    public string? Name { get; set; }
    /// <summary>Nonblank replacement text, preserved exactly; never logged.</summary>
    public string? Value { get; set; }
}

/// <summary>Removal of one ordinary attribute by name.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RemoveAttributeInput
{
    /// <summary>Nonblank lookup name, trimmed and compared ignoring ordinal case.</summary>
    public string? Name { get; set; }
}
