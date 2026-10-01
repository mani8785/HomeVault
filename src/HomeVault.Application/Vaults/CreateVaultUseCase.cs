using HomeVault.Application.Identity;
using HomeVault.Domain.Vaults;

namespace HomeVault.Application.Vaults;

/// <summary>Creates a Vault owned by the current actor and inserts it through the repository boundary.</summary>
public sealed class CreateVaultUseCase
{
    private readonly ICurrentActor _currentActor;
    private readonly IVaultRepository _repository;

    /// <summary>Constructs the use case with explicit trusted identity and storage collaborators.</summary>
    /// <param name="currentActor">The identity context read for each execution.</param>
    /// <param name="repository">The storage adapter that atomically inserts the new Vault.</param>
    /// <exception cref="ArgumentNullException">The collaborator is null.</exception>
    public CreateVaultUseCase(ICurrentActor currentActor, IVaultRepository repository)
    {
        ArgumentNullException.ThrowIfNull(currentActor);
        ArgumentNullException.ThrowIfNull(repository);
        _currentActor = currentActor;
        _repository = repository;
    }

    /// <summary>Creates a Vault with the current actor as its sole initial Owner.</summary>
    /// <param name="request">The proposed Vault metadata; it cannot nominate an owner.</param>
    /// <param name="cancellationToken">Cancellation propagated to storage; pre-cancellation stops before reading identity.</param>
    /// <returns>An immutable view or safe failure. Missing identity takes precedence over request-field validation; Domain then checks id, name, and type.</returns>
    /// <exception cref="ArgumentNullException">The request object is null, a caller programming error.</exception>
    /// <exception cref="InvalidOperationException">Domain or the repository returns an unexpected outcome that the application does not map.</exception>
    /// <exception cref="OperationCanceledException">The operation is cancelled.</exception>
    /// <remarks>Reads identity once per call. Success means adapter acceptance, not durable storage. Collaborator exceptions propagate without retries; a failed response does not prove no write occurred.</remarks>
    public async Task<CreateVaultResult> ExecuteAsync(CreateVaultRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var actorId = _currentActor.ActorId;
        if (actorId is null || actorId == Guid.Empty)
        {
            return new CreateVaultResult(CreateVaultError.Unauthenticated);
        }

        var result = Vault.Create(request.Id, request.Name, request.Type, actorId.Value);
        if (result.Vault is { } vault)
        {
            var outcome = await _repository.AddAsync(vault, cancellationToken);
            return outcome switch
            {
                VaultAddOutcome.Added => new CreateVaultResult(new CreatedVault(vault)),
                VaultAddOutcome.IdentityConflict => new CreateVaultResult(CreateVaultError.IdentityConflict),
                _ => throw new InvalidOperationException("Unexpected Vault insertion outcome.")
            };
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
