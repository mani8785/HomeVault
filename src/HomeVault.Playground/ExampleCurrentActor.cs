using HomeVault.Application.Identity;

namespace HomeVault.Playground;

// Fictional local identity context only; a production adapter must use verified authentication.
internal sealed class ExampleCurrentActor(Guid? actorId) : ICurrentActor
{
    public Guid? ActorId { get; } = actorId;
}
