namespace HomeVault.Application.Vaults;

/// <summary>Describes the expected outcome of an atomic Vault insertion without exposing stored metadata.</summary>
public enum VaultAddOutcome
{
    /// <summary>The adapter accepted the complete Vault and initial membership; durability depends on the adapter.</summary>
    Added,
    /// <summary>The identity already exists; existing data was preserved regardless of ownership or matching input.</summary>
    IdentityConflict
}
