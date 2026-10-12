namespace HomeVault.Domain;

/// <summary>Safe failures from replacing a record name; never contains names.</summary>
public enum RenameError
{
    /// <summary>The name was replaced, or already matched the requested value.</summary>
    None,
    /// <summary>The requested name is null, empty or whitespace.</summary>
    BlankName,
    /// <summary>The Vault is read-only, including unchanged-name requests.</summary>
    Archived
}
