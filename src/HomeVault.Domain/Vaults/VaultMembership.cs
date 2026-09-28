namespace HomeVault.Domain.Vaults;

/// <summary>An immutable actor membership owned by a Vault.</summary>
public sealed class VaultMembership
{
    internal VaultMembership(Guid actorId, VaultRole role)
    {
        ActorId = actorId;
        Role = role;
    }

    /// <summary>Gets the actor identity, not an Asset identity. Avoid exposing private identifiers.</summary>
    public Guid ActorId { get; }

    /// <summary>Gets the assigned domain role.</summary>
    public VaultRole Role { get; }

    /// <summary>Returns a label without exposing the actor identifier.</summary>
    /// <returns>The label VaultMembership.</returns>
    public override string ToString() => nameof(VaultMembership);
}
