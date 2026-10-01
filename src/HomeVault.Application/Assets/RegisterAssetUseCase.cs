using HomeVault.Application.Identity;
using HomeVault.Domain.Assets;

namespace HomeVault.Application.Assets;

/// <summary>Registers a Vault-bound Asset on behalf of the current actor.</summary>
public sealed class RegisterAssetUseCase
{
    private readonly ICurrentActor _actor;
    private readonly IAssetRegistrationStore _store;

    /// <summary>Constructs the use case with explicit identity and atomic storage boundaries.</summary>
    /// <param name="actor">Trusted identity context, read once per execution.</param>
    /// <param name="store">The adapter enforcing access and insertion atomically.</param>
    /// <exception cref="ArgumentNullException">Either collaborator is null.</exception>
    public RegisterAssetUseCase(ICurrentActor actor, IAssetRegistrationStore store)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(store);
        _actor = actor;
        _store = store;
    }

    /// <summary>Validates the proposed Asset and awaits atomic registration.</summary>
    /// <param name="request">The proposed identity, Vault, and name.</param>
    /// <param name="cancellationToken">Cancellation propagated to the adapter.</param>
    /// <returns>An immutable view or safe error; validation precedes storage access.</returns>
    /// <exception cref="ArgumentNullException">The request is null.</exception>
    /// <exception cref="OperationCanceledException">Execution is cancelled.</exception>
    /// <exception cref="InvalidOperationException">A collaborator returns an unmapped outcome.</exception>
    /// <remarks>Checks pre-cancellation, actor, Asset id, Vault id, then name. Unexpected failures propagate without retry or logging; success does not imply durable storage.</remarks>
    public async Task<RegisterAssetResult> ExecuteAsync(RegisterAssetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var actorId = _actor.ActorId;
        if (actorId is null || actorId == Guid.Empty)
            return new RegisterAssetResult(RegisterAssetError.Unauthenticated);
        var creation = Asset.Create(request.Id, request.VaultId, request.Name);
        if (creation.Asset is not { } asset)
            return new RegisterAssetResult(creation.Error switch
            {
                AssetCreationError.EmptyIdentity => RegisterAssetError.EmptyIdentity,
                AssetCreationError.EmptyVaultIdentity => RegisterAssetError.EmptyVaultIdentity,
                AssetCreationError.BlankName => RegisterAssetError.BlankName,
                _ => throw new InvalidOperationException("Unexpected Asset creation outcome.")
            });
        var outcome = await _store.RegisterAsync(asset, actorId.Value, cancellationToken);
        return outcome switch
        {
            AssetRegistrationOutcome.Added => new RegisterAssetResult(new RegisteredAsset(asset)),
            AssetRegistrationOutcome.VaultUnavailable => new RegisterAssetResult(RegisterAssetError.VaultUnavailable),
            AssetRegistrationOutcome.Forbidden => new RegisterAssetResult(RegisterAssetError.Forbidden),
            AssetRegistrationOutcome.VaultArchived => new RegisterAssetResult(RegisterAssetError.VaultArchived),
            AssetRegistrationOutcome.IdentityConflict => new RegisterAssetResult(RegisterAssetError.IdentityConflict),
            _ => throw new InvalidOperationException("Unexpected Asset registration outcome.")
        };
    }
}
