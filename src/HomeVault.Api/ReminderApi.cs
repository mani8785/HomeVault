using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HomeVault.Application.Reminders;
using HomeVault.Domain.Reminders;

namespace HomeVault.Api;

internal static partial class ReminderApi
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/vaults/{vaultId}/reminders").RequireAuthorization();
        group.MapPost("", async (string vaultId, CreateReminderInput input, ReminderUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Identity(input.Id, out var id) || !Identity(input.AssetId, out var asset)) return ApiProblems.Result(400, "invalid_identity");
            if (!Instant(input.DueAt, out var dueAt)) return ApiProblems.Result(400, "invalid_due_at");
            var outcome = await useCases.CreateAsync(vault, id, asset, input.Action, dueAt, token);
            return outcome == ReminderOutcome.Succeeded
                ? Results.Created($"/api/v1/vaults/{vault:D}/reminders/{id:D}", Metadata(new ReminderSnapshot(id, vault, asset, dueAt, ReminderStatus.Pending, input.Action!)))
                : Failure(outcome);
        });
        group.MapGet("/{reminderId}", async (string vaultId, string reminderId, ReminderUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Identity(reminderId, out var id)) return ApiProblems.Result(400, "invalid_identity");
            var root = await useCases.FindAsync(vault, id, token);
            return root is null ? ApiProblems.Result(404) : Results.Ok(Metadata(root));
        });
        group.MapGet("/{reminderId}/action", async (string vaultId, string reminderId, ReminderUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Identity(reminderId, out var id)) return ApiProblems.Result(400, "invalid_identity");
            var root = await useCases.FindAsync(vault, id, token);
            return root is null ? ApiProblems.Result(404) : Results.Ok(new { action = root.ReadAction() });
        });
        group.MapPut("/{reminderId}", async (string vaultId, string reminderId, UpdateReminderInput input, ReminderUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Identity(reminderId, out var id)) return ApiProblems.Result(400, "invalid_identity");
            if (!Instant(input.DueAt, out var dueAt)) return ApiProblems.Result(400, "invalid_due_at");
            var outcome = await useCases.UpdateAsync(vault, id, input.Action, dueAt, token);
            return outcome == ReminderOutcome.Succeeded ? Results.NoContent() : Failure(outcome);
        });
        group.MapPost("/{reminderId}/complete", (string vaultId, string reminderId, HttpContext context, ReminderUseCases useCases, CancellationToken token) => Finish(vaultId, reminderId, context, useCases, true, token));
        group.MapPost("/{reminderId}/cancel", (string vaultId, string reminderId, HttpContext context, ReminderUseCases useCases, CancellationToken token) => Finish(vaultId, reminderId, context, useCases, false, token));
    }
    private static async Task<IResult> Finish(string vaultId, string reminderId, HttpContext context, ReminderUseCases useCases, bool complete, CancellationToken token)
    {
        if (await context.Request.Body.ReadAsync(new byte[1], token) != 0) return ApiProblems.Result(400);
        if (!Identity(vaultId, out var vault) || !Identity(reminderId, out var id)) return ApiProblems.Result(400, "invalid_identity");
        var outcome = complete ? await useCases.CompleteAsync(vault, id, token) : await useCases.CancelAsync(vault, id, token);
        return outcome == ReminderOutcome.Succeeded ? Results.NoContent() : Failure(outcome);
    }
    private static object Metadata(ReminderSnapshot root) => new
    {
        id = root.Id,
        vaultId = root.VaultId,
        assetId = root.AssetId,
        dueAt = root.DueAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
        status = root.Status switch { ReminderStatus.Pending => "pending", ReminderStatus.Completed => "completed", _ => "cancelled" }
    };
    private static bool Identity(string? value, out Guid id) => Guid.TryParseExact(value, "D", out id) && id != Guid.Empty;
    private static bool Instant(string? value, out DateTimeOffset instant)
    {
        instant = default;
        if (value is null || !Timestamp().IsMatch(value) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        instant = parsed.ToUniversalTime(); return true;
    }
    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Timestamp();
    private static IResult Failure(ReminderOutcome outcome) => outcome switch
    {
        ReminderOutcome.Unauthenticated => ApiProblems.Result(401),
        ReminderOutcome.InvalidIdentity => ApiProblems.Result(400, "invalid_identity"),
        ReminderOutcome.Unavailable => ApiProblems.Result(404),
        ReminderOutcome.Forbidden => ApiProblems.Result(403),
        ReminderOutcome.Archived => ApiProblems.Result(409, "vault_archived"),
        ReminderOutcome.BlankAction => ApiProblems.Result(400, "blank_action"),
        ReminderOutcome.IdentityConflict => ApiProblems.Result(409, "identity_conflict"),
        ReminderOutcome.NotPending => ApiProblems.Result(409, "not_pending"),
        _ => ApiProblems.Result(500)
    };
}

/// <summary>Caller-identified Reminder creation; private action is never echoed by creation responses.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateReminderInput
{
    /// <summary>Nonempty D-format root identity.</summary>
    public string? Id { get; set; }
    /// <summary>Nonempty D-format actual same-Vault Asset identity.</summary>
    public string? AssetId { get; set; }
    /// <summary>Private nonblank action preserved exactly.</summary>
    public string? Action { get; set; }
    /// <summary>Required ISO timestamp with seconds and explicit offset, at most seven fractional digits.</summary>
    public string? DueAt { get; set; }
    /// <summary>Returns a label without private input.</summary><returns>A constant safe label.</returns>
    public override string ToString() => nameof(CreateReminderInput);
}

/// <summary>Atomic replacement of the two mutable Pending fields.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateReminderInput
{
    /// <summary>Private nonblank action preserved exactly.</summary>
    public string? Action { get; set; }
    /// <summary>Required exact ISO timestamp with explicit offset.</summary>
    public string? DueAt { get; set; }
    /// <summary>Returns a label without private input.</summary><returns>A constant safe label.</returns>
    public override string ToString() => nameof(UpdateReminderInput);
}
