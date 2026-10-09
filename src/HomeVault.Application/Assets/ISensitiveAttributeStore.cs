using HomeVault.Domain.Assets;

namespace HomeVault.Application.Assets;

/// <summary>Atomic current-access boundary; authorize before loading or decrypting private text.</summary>
public interface ISensitiveAttributeStore
{
    /// <summary>Adds explicitly Sensitive text under a generated stable identity.</summary>
    /// <param name="assetId">Target Asset.</param><param name="actorId">Trusted current actor.</param>
    /// <param name="name">Visible label.</param><param name="value">Private text.</param><param name="sensitivity">Required Sensitive classification.</param>
    /// <param name="token">Cancellation.</param><returns>A safe result and new identity on success.</returns>
    Task<SensitiveAttributeResult> AddAsync(Guid assetId, Guid actorId, string? name, string? value, AttributeSensitivity sensitivity, CancellationToken token);
    /// <summary>Changes one value after role/archive checks, retaining identity and label.</summary>
    /// <param name="assetId">Actual owning Asset.</param><param name="actorId">Trusted actor.</param><param name="attributeId">Stable target identity.</param><param name="value">Replacement private text.</param><param name="token">Cancellation.</param><returns>A safe result.</returns>
    Task<SensitiveAttributeResult> ChangeAsync(Guid assetId, Guid actorId, Guid attributeId, string? value, CancellationToken token);
    /// <summary>Removes one Sensitive row without decrypting its old value.</summary>
    /// <param name="assetId">Actual owning Asset.</param><param name="actorId">Trusted actor.</param><param name="attributeId">Target identity.</param><param name="token">Cancellation.</param><returns>A safe result.</returns>
    Task<SensitiveAttributeResult> RemoveAsync(Guid assetId, Guid actorId, Guid attributeId, CancellationToken token);
    /// <summary>Returns member-visible metadata without selecting ciphertext or accessing keys.</summary>
    /// <param name="assetId">Target Asset.</param><param name="actorId">Trusted actor.</param><param name="token">Cancellation.</param><returns>A safe result and metadata snapshot.</returns>
    Task<SensitiveAttributeResult> ListAsync(Guid assetId, Guid actorId, CancellationToken token);
    /// <summary>Reads one value only after current Sensitive permission checks in a consistent transaction.</summary>
    /// <param name="assetId">Actual owning Asset.</param><param name="actorId">Trusted actor.</param><param name="attributeId">Target identity.</param><param name="token">Cancellation.</param><returns>A deliberate value-read result or safe failure.</returns>
    Task<SensitiveAttributeResult> ReadAsync(Guid assetId, Guid actorId, Guid attributeId, CancellationToken token);
}
