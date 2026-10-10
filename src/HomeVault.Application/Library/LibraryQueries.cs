using HomeVault.Application.Identity;

namespace HomeVault.Application.Library;

/// <summary>A bounded page of permitted metadata, without a global record count.</summary>
/// <typeparam name="T">Metadata projection; never private content.</typeparam>
/// <param name="Items">Records in stable identity order.</param>
/// <param name="HasMore">Another page existed in this read snapshot.</param>
public sealed record LibraryPage<T>(IReadOnlyList<T> Items, bool HasMore);

/// <summary>Validated offset-page options; concurrent writes may shift subsequent pages.</summary>
/// <param name="Offset">Nonnegative row offset.</param>
/// <param name="Limit">Page size from one to one hundred.</param>
/// <param name="Search">Optional visible-name substring, at most 200 characters.</param>
public sealed record LibraryQuery(int Offset = 0, int Limit = 50, string Search = "")
{
    /// <summary>Whether these options meet the bounded query contract.</summary>
    public bool IsValid => Offset >= 0 && Limit is >= 1 and <= 100 && Search is { Length: <= 200 };
}

/// <summary>Visible Vault metadata and the current caller's role.</summary>
/// <param name="Id">Vault identity.</param><param name="Name">Ordinary display name.</param>
/// <param name="Type">Domain Vault type.</param><param name="Status">Domain lifecycle state.</param>
/// <param name="Role">Current caller's membership role.</param>
public sealed record LibraryVault(Guid Id, string Name, int Type, int Status, int Role);

/// <summary>Visible Asset metadata without attributes or private content.</summary>
/// <param name="Id">Asset identity.</param><param name="VaultId">Owning Vault.</param><param name="Name">Ordinary display name.</param>
public sealed record LibraryAsset(Guid Id, Guid VaultId, string Name);

/// <summary>Membership metadata without account names or email addresses.</summary>
/// <param name="ActorId">Existing member identity.</param><param name="Role">Current domain role.</param>
public sealed record LibraryMember(Guid ActorId, int Role);

/// <summary>Directed Relationship metadata.</summary>
/// <param name="Id">Relationship identity.</param><param name="SourceAssetId">Source record.</param>
/// <param name="TargetAssetId">Target record.</param><param name="Status">Domain lifecycle state.</param>
public sealed record LibraryRelationship(Guid Id, Guid SourceAssetId, Guid TargetAssetId, int Status);

/// <summary>Reminder metadata; excludes private action text.</summary>
/// <param name="Id">Reminder identity.</param><param name="AssetId">Owning record.</param>
/// <param name="DueAtUtcTicks">UTC instant as ticks.</param><param name="Status">Domain lifecycle state.</param>
public sealed record LibraryReminder(Guid Id, Guid AssetId, long DueAtUtcTicks, int Status);

/// <summary>Read-only current-membership projections. Filter access before paging; return null for unavailable scope.</summary>
/// <remarks>Implementations must validate options and read permission and rows consistently. Never return private content.</remarks>
public interface ILibraryQueries
{
    /// <summary>Lists only the actor's Vaults, including archived Vaults.</summary>
    Task<LibraryPage<LibraryVault>?> VaultsAsync(Guid actor, LibraryQuery query, CancellationToken token);
    /// <summary>Lists Assets of a currently accessible Vault.</summary>
    Task<LibraryPage<LibraryAsset>?> AssetsAsync(Guid actor, Guid vault, LibraryQuery query, CancellationToken token);
    /// <summary>Lists membership identities only for a current Owner or Administrator.</summary>
    Task<LibraryPage<LibraryMember>?> MembersAsync(Guid actor, Guid vault, LibraryQuery query, CancellationToken token);
    /// <summary>Lists permitted Relationship metadata, optionally involving one Asset.</summary>
    Task<LibraryPage<LibraryRelationship>?> RelationshipsAsync(Guid actor, Guid vault, Guid? asset, LibraryQuery query, CancellationToken token);
    /// <summary>Lists permitted Reminder metadata, optionally belonging to one Asset, without reading action text.</summary>
    Task<LibraryPage<LibraryReminder>?> RemindersAsync(Guid actor, Guid vault, Guid? asset, LibraryQuery query, CancellationToken token);
}

/// <summary>Supplies trusted current identity to the library boundary; request fields cannot choose the actor.</summary>
/// <param name="actor">Trusted authenticated context.</param><param name="queries">Membership-filtered storage adapter.</param>
public sealed class BrowseLibrary(ICurrentActor actor, ILibraryQueries queries)
{
    /// <summary>Reads the caller's visible Vault page, or null without calling storage when unauthenticated.</summary>
    public Task<LibraryPage<LibraryVault>?> VaultsAsync(LibraryQuery query, CancellationToken token) =>
        Run(id => queries.VaultsAsync(id, query, token), token);
    /// <summary>Reads a permitted Asset page.</summary>
    public Task<LibraryPage<LibraryAsset>?> AssetsAsync(Guid vault, LibraryQuery query, CancellationToken token) =>
        Run(id => queries.AssetsAsync(id, vault, query, token), token);
    /// <summary>Reads a permitted member page.</summary>
    public Task<LibraryPage<LibraryMember>?> MembersAsync(Guid vault, LibraryQuery query, CancellationToken token) =>
        Run(id => queries.MembersAsync(id, vault, query, token), token);
    /// <summary>Reads a permitted Relationship page.</summary>
    public Task<LibraryPage<LibraryRelationship>?> RelationshipsAsync(Guid vault, Guid? asset, LibraryQuery query, CancellationToken token) =>
        Run(id => queries.RelationshipsAsync(id, vault, asset, query, token), token);
    /// <summary>Reads a permitted Reminder page without action text.</summary>
    public Task<LibraryPage<LibraryReminder>?> RemindersAsync(Guid vault, Guid? asset, LibraryQuery query, CancellationToken token) =>
        Run(id => queries.RemindersAsync(id, vault, asset, query, token), token);

    private Task<LibraryPage<T>?> Run<T>(Func<Guid, Task<LibraryPage<T>?>> read, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = actor.ActorId;
        return id is null || id == Guid.Empty ? Task.FromResult<LibraryPage<T>?>(null) : read(id.Value);
    }
}
