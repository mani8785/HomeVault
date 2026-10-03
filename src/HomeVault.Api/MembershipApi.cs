using System.Text.Json.Serialization;
using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;

namespace HomeVault.Api;

internal static class MembershipApi
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/vaults/{vaultId}/members").RequireAuthorization();
        group.MapPost("", async (string vaultId, AddMemberInput input, VaultMembershipUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Identity(input.TargetActorId, out var target)) return ApiProblems.Result(400, "invalid_identity");
            return Result(await useCases.AddAsync(vault, target, Role(input.Role), token));
        });
        group.MapPut("/{targetActorId}/role", async (string vaultId, string targetActorId, ChangeMemberRoleInput input, VaultMembershipUseCases useCases, CancellationToken token) =>
        {
            if (!Identity(vaultId, out var vault) || !Identity(targetActorId, out var target)) return ApiProblems.Result(400, "invalid_identity");
            return Result(await useCases.ChangeRoleAsync(vault, target, Role(input.Role), token));
        });
        group.MapDelete("/{targetActorId}", async (string vaultId, string targetActorId, HttpContext context, VaultMembershipUseCases useCases, CancellationToken token) =>
        {
            if (await context.Request.Body.ReadAsync(new byte[1], token) != 0) return ApiProblems.Result(400);
            if (!Identity(vaultId, out var vault) || !Identity(targetActorId, out var target)) return ApiProblems.Result(400, "invalid_identity");
            return Result(await useCases.RemoveAsync(vault, target, token));
        });
    }

    private static bool Identity(string? text, out Guid value) => Guid.TryParseExact(text, "D", out value) && value != Guid.Empty;
    private static VaultRole Role(string? role) => role switch
    {
        "owner" => VaultRole.Owner,
        "administrator" => VaultRole.Administrator,
        "editor" => VaultRole.Editor,
        "viewer" => VaultRole.Viewer,
        _ => (VaultRole)(-1)
    };

    private static IResult Result(MembershipOutcome result) => result switch
    {
        MembershipOutcome.Succeeded => Results.NoContent(),
        MembershipOutcome.Unauthenticated => ApiProblems.Result(401),
        MembershipOutcome.InvalidIdentity => ApiProblems.Result(400, "invalid_identity"),
        MembershipOutcome.InvalidRole => ApiProblems.Result(400, "invalid_role", "role"),
        MembershipOutcome.Unavailable => ApiProblems.Result(404),
        MembershipOutcome.Forbidden => ApiProblems.Result(403),
        MembershipOutcome.Archived => ApiProblems.Result(409, "vault_archived"),
        MembershipOutcome.DuplicateMember => ApiProblems.Result(409, "duplicate_member"),
        MembershipOutcome.LastOwner => ApiProblems.Result(409, "last_owner"),
        _ => ApiProblems.Result(500)
    };
}

/// <summary>Membership addition input; requesting identity is never accepted from the client.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AddMemberInput
{
    /// <summary>Existing enabled target account Guid in D format; not the requesting actor.</summary>
    public string? TargetActorId { get; set; }
    /// <summary>Lowercase owner, administrator, editor or viewer.</summary>
    public string? Role { get; set; }
}

/// <summary>Role-change input; target membership comes from the route.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeMemberRoleInput
{
    /// <summary>Lowercase replacement role: owner, administrator, editor or viewer.</summary>
    public string? Role { get; set; }
}
