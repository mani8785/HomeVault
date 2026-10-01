using HomeVault.Domain.Vaults;

namespace HomeVault.Application.Vaults;

/// <summary>An immutable view of a transiently created Vault, without exposing domain mutation methods.</summary>
/// <remarks>Contains private metadata for deliberate client reads. This view is not proof of persistence or authorization.</remarks>
public sealed class CreatedVault
{
    internal CreatedVault(Vault vault)
    {
        Id = vault.Id;
        Name = vault.Name;
        Type = vault.Type;
        Status = vault.Status;
        InitialOwnerId = vault.Memberships.Single().ActorId;
    }

    /// <summary>Gets the supplied Vault identity.</summary>
    public Guid Id { get; }

    /// <summary>Gets the original validated name; do not log private names.</summary>
    public string Name { get; }

    /// <summary>Gets the validated ownership context.</summary>
    public VaultType Type { get; }

    /// <summary>Gets the creation state, Active.</summary>
    public VaultStatus Status { get; }

    /// <summary>Gets the current actor identity assigned the initial Owner role.</summary>
    public Guid InitialOwnerId { get; }

    /// <summary>Returns a safe label without private metadata.</summary>
    /// <returns>The label CreatedVault.</returns>
    public override string ToString() => nameof(CreatedVault);
}
