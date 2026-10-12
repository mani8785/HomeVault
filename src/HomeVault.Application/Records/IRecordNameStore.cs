namespace HomeVault.Application.Records;

/// <summary>Atomic record renaming with current membership, role, archive and expected-name checks.</summary>
/// <remarks>Check access before validating names. Preserve related rows and return safe failures without values.</remarks>
public interface IRecordNameStore
{
    /// <summary>Renames an Active Vault for a current Owner or Administrator.</summary>
    /// <param name="vaultId">Nonempty target identity.</param><param name="actorId">Trusted requester.</param>
    /// <param name="name">Replacement name, preserved exactly.</param><param name="expectedName">Exact last-read name.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe outcome; rejected writes preserve state.</returns>
    Task<RenameOutcome> RenameVaultAsync(Guid vaultId, Guid actorId, string? name, string? expectedName, CancellationToken cancellationToken);

    /// <summary>Renames an Asset in its actual Active Vault for a current Owner, Administrator or Editor.</summary>
    /// <param name="assetId">Nonempty target identity.</param><param name="actorId">Trusted requester.</param>
    /// <param name="name">Replacement name, preserved exactly.</param><param name="expectedName">Exact last-read name.</param>
    /// <param name="cancellationToken">Operation cancellation.</param><returns>A safe outcome; never loads Sensitive values.</returns>
    Task<RenameOutcome> RenameAssetAsync(Guid assetId, Guid actorId, string? name, string? expectedName, CancellationToken cancellationToken);
}
