using HomeVault.Domain.Reminders;

namespace HomeVault.Application.Reminders;

/// <summary>Immutable authorized snapshot; private action requires deliberate access.</summary>
/// <param name="id">Root identity.</param><param name="vaultId">Owning Vault.</param>
/// <param name="assetId">Referenced Asset.</param><param name="dueAt">UTC due instant.</param>
/// <param name="status">Lifecycle state.</param><param name="action">Private action, never included in default serialization.</param>
public sealed class ReminderSnapshot(Guid id, Guid vaultId, Guid assetId, DateTimeOffset dueAt, ReminderStatus status, string action)
{
    /// <summary>Gets the root identity.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the owning Vault.</summary>
    public Guid VaultId { get; } = vaultId;
    /// <summary>Gets the referenced Asset.</summary>
    public Guid AssetId { get; } = assetId;
    /// <summary>Gets the exact UTC instant.</summary>
    public DateTimeOffset DueAt { get; } = dueAt;
    /// <summary>Gets the stored lifecycle.</summary>
    public ReminderStatus Status { get; } = status;
    /// <summary>Deliberately reads private action text after authorization.</summary>
    /// <returns>Original action text; do not log it.</returns>
    public string ReadAction() => action;
    /// <summary>Returns a safe label without private data.</summary>
    /// <returns>A constant label.</returns>
    public override string ToString() => nameof(ReminderSnapshot);
}
