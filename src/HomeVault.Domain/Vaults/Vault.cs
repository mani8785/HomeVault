using Ardalis.GuardClauses;

namespace HomeVault.Domain.Vaults;

/// <summary>An ownership boundary created with one initial Owner and no loaded Assets.</summary>
/// <remarks>Creation does not persist, resolve actor identities, or authorize access.</remarks>
public sealed class Vault
{
    private readonly IReadOnlyList<VaultMembership> _memberships;

    private Vault(Guid id, string? name, VaultType type, Guid initialOwnerId)
    {
        Id = Guard.Against.NullOrEmpty(id, nameof(id));
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Type = Guard.Against.EnumOutOfRange(type, nameof(type));
        var ownerId = Guard.Against.NullOrEmpty(initialOwnerId, nameof(initialOwnerId));
        _memberships = Array.AsReadOnly(new[] { new VaultMembership(ownerId, VaultRole.Owner) });
    }

    /// <summary>Gets the caller-supplied non-empty Vault identity.</summary>
    public Guid Id { get; }

    /// <summary>Gets the nonblank name exactly as supplied. Names may be private; avoid logging them.</summary>
    public string Name { get; }

    /// <summary>Gets the ownership context.</summary>
    public VaultType Type { get; }

    /// <summary>Gets the lifecycle state, initially Active. Archival operations are not implemented yet.</summary>
    public VaultStatus Status { get; } = VaultStatus.Active;

    /// <summary>Gets an immutable snapshot containing the initial Owner membership.</summary>
    /// <remarks>Membership mutations are a subsequent use case. No Asset collection is loaded.</remarks>
    public IReadOnlyList<VaultMembership> Memberships => _memberships;

    /// <summary>Creates an Active Vault with exactly one initial Owner, or returns a validation failure.</summary>
    /// <param name="id">A non-empty caller-supplied Vault identity.</param>
    /// <param name="name">Nonblank text preserved without trimming or normalization.</param>
    /// <param name="type">Personal, Household, or Organization.</param>
    /// <param name="initialOwnerId">A non-empty actor identity; existence is not checked here.</param>
    /// <returns>A complete Vault on success, otherwise an error with no Vault. Inputs are validated in parameter order.</returns>
    /// <remarks>Does not generate identities, check uniqueness, persist data, or emit events. Errors omit supplied values.</remarks>
    public static VaultCreationResult Create(Guid id, string? name, VaultType type, Guid initialOwnerId)
    {
        try
        {
            return new VaultCreationResult(new Vault(id, name, type, initialOwnerId));
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(id))
        {
            return new VaultCreationResult(VaultCreationError.EmptyIdentity);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(name))
        {
            return new VaultCreationResult(VaultCreationError.BlankName);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(type))
        {
            return new VaultCreationResult(VaultCreationError.InvalidType);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(initialOwnerId))
        {
            return new VaultCreationResult(VaultCreationError.EmptyOwnerIdentity);
        }
    }

    /// <summary>Returns a safe label without the name or identifiers.</summary>
    /// <returns>The label Vault.</returns>
    public override string ToString() => nameof(Vault);
}
