namespace HomeVault.Application.Identity;

/// <summary>Supplies the current actor identity from a trusted execution context.</summary>
/// <remarks>Production adapters must use verified authentication context, never an arbitrary request field. This contract itself does not authenticate.</remarks>
public interface ICurrentActor
{
    /// <summary>Gets the current actor identity, or null when unavailable. Empty identities are treated as unauthenticated.</summary>
    Guid? ActorId { get; }
}
