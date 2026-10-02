using System.Security.Claims;
using HomeVault.Application.Identity;

namespace HomeVault.Api;

// Called only after cookie authentication has validated the account and stamp.
internal sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    public Guid? ActorId
    {
        get
        {
            var identity = accessor.HttpContext?.User.Identities.FirstOrDefault(identity =>
                identity.IsAuthenticated && identity.AuthenticationType == AuthenticationHost.Scheme);
            return Guid.TryParse(identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var actor) && actor != Guid.Empty
                ? actor : null;
        }
    }
}
