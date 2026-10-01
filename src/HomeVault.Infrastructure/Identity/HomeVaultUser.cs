using Microsoft.AspNetCore.Identity;

namespace HomeVault.Infrastructure.Identity;

/// <summary>An Infrastructure account whose immutable-in-use Guid identifies a Vault actor.</summary>
/// <remarks>
/// Construct through Identity's account workflow. Creation alone grants no Vault membership.
/// Never serialize or log this storage entity; it contains credential and account metadata.
/// Existing fictional memberships are not automatically assigned to new accounts.
/// </remarks>
public sealed class HomeVaultUser : IdentityUser<Guid>
{
    /// <summary>Allocates a new account identity without creating a credential or membership.</summary>
    public HomeVaultUser()
    {
        Id = Guid.NewGuid();
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    /// <summary>Gets or sets whether authentication may use this account.</summary>
    /// <remarks>The authentication slice must check this and invalidate the security stamp on disable.</remarks>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Returns a label without identity, login name, or credential metadata.</summary>
    /// <returns>A constant safe label.</returns>
    public override string ToString() => nameof(HomeVaultUser);
}
