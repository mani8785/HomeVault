using System.Text.Json.Serialization;

namespace HomeVault.Api;

/// <summary>Vault creation input. Actor, owner, identity and other unknown fields are rejected.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class VaultInput
{
    /// <summary>Private name; null/blank names are rejected by the application.</summary>
    public string? Name { get; set; }
    /// <summary>Required lowercase ownership context: personal, household or organization.</summary>
    public string? Type { get; set; }
}

/// <summary>Asset registration input. Its Vault comes from the route and identity comes from the server.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AssetInput
{
    /// <summary>Private name; null/blank names are rejected by the application.</summary>
    public string? Name { get; set; }
}

/// <summary>Created Vault transport metadata; contains no membership list or Domain object.</summary>
public sealed class VaultOutput
{
    /// <summary>Server-generated Vault identifier, serialized in Guid D format.</summary>
    public required Guid Id { get; init; }
    /// <summary>Original validated private name; do not log.</summary>
    public required string Name { get; init; }
    /// <summary>Lowercase personal, household or organization.</summary>
    public required string Type { get; init; }
    /// <summary>The creation state, active.</summary>
    public string Status => "active";
}

/// <summary>Asset metadata returned after successful creation or current-membership inspection.</summary>
public sealed class AssetOutput
{
    /// <summary>Server-generated Asset identifier in Guid D format.</summary>
    public required Guid Id { get; init; }
    /// <summary>Owning Vault identifier in Guid D format.</summary>
    public required Guid VaultId { get; init; }
    /// <summary>Original private name; authorization is required for each read.</summary>
    public required string Name { get; init; }
}
