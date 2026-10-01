using HomeVault.Domain.Vaults;

namespace HomeVault.Application.Vaults;

/// <summary>Inputs for transient Vault creation; ownership comes from the current actor.</summary>
public sealed class CreateVaultRequest
{
    /// <summary>Captures inputs without validating them; the use case returns safe validation failures.</summary>
    /// <param name="id">The caller-supplied Vault identity.</param>
    /// <param name="name">The proposed name, which may be private.</param>
    /// <param name="type">The proposed ownership context.</param>
    public CreateVaultRequest(Guid id, string? name, VaultType type)
    {
        Id = id;
        Name = name;
        Type = type;
    }

    /// <summary>Gets the proposed Vault identity.</summary>
    public Guid Id { get; }

    /// <summary>Gets the proposed name; avoid logging it.</summary>
    public string? Name { get; }

    /// <summary>Gets the proposed ownership context.</summary>
    public VaultType Type { get; }

    /// <summary>Returns a safe label without formatting inputs.</summary>
    /// <returns>The label CreateVaultRequest.</returns>
    public override string ToString() => nameof(CreateVaultRequest);
}
