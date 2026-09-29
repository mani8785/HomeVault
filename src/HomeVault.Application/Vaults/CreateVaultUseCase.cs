using HomeVault.Application.Identity;
using HomeVault.Domain.Vaults;

namespace HomeVault.Application.Vaults;

/// <summary>Creates a transient Vault owned by the current actor through the accepted domain factory.</summary>
public sealed class CreateVaultUseCase
{
    private readonly ICurrentActor _currentActor;

    /// <summary>Constructs the use case with an explicit trusted identity collaborator.</summary>
    /// <param name="currentActor">The identity context read for each execution.</param>
    /// <exception cref="ArgumentNullException">The collaborator is null.</exception>
    public CreateVaultUseCase(ICurrentActor currentActor)
    {
        ArgumentNullException.ThrowIfNull(currentActor);
        _currentActor = currentActor;
    }

    /// <summary>Creates a Vault with the current actor as its sole initial Owner.</summary>
    /// <param name="request">The proposed Vault metadata; it cannot nominate an owner.</param>
    /// <returns>An immutable view or safe failure. Missing identity takes precedence over request-field validation; Domain then checks id, name, and type.</returns>
    /// <exception cref="ArgumentNullException">The request object is null, a caller programming error.</exception>
    /// <exception cref="InvalidOperationException">Domain returns an unexpected failure that the application does not map.</exception>
    /// <remarks>Reads identity once per call. Does not authenticate, persist, check uniqueness, or cache an actor across calls. Collaborator exceptions propagate.</remarks>
    public CreateVaultResult Execute(CreateVaultRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorId = _currentActor.ActorId;
        if (actorId is null || actorId == Guid.Empty)
        {
            return new CreateVaultResult(CreateVaultError.Unauthenticated);
        }

        var result = Vault.Create(request.Id, request.Name, request.Type, actorId.Value);
        if (result.Vault is { } vault)
        {
            return new CreateVaultResult(new CreatedVault(vault));
        }

        return new CreateVaultResult(result.Error switch
        {
            VaultCreationError.EmptyIdentity => CreateVaultError.EmptyIdentity,
            VaultCreationError.BlankName => CreateVaultError.BlankName,
            VaultCreationError.InvalidType => CreateVaultError.InvalidType,
            _ => throw new InvalidOperationException("Unexpected Vault creation outcome.")
        });
    }
}
