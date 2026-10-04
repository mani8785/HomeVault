using HomeVault.Application.Assets;
using HomeVault.Application.Identity;
using HomeVault.Application.Relationships;
using HomeVault.Application.Vaults;
using HomeVault.Domain.Vaults;
using HomeVault.Infrastructure.Persistence;

namespace HomeVault.Api;

/// <summary>Composes authenticated Vault/Asset use cases and their versioned HTTP contract.</summary>
public static class RecordsApi
{
    /// <summary>Registers scoped use cases and actor context with the existing SQLite adapters.</summary>
    /// <param name="services">Host composition services; authentication must also be configured.</param>
    /// <param name="databasePath">The same existing migrated database used for Identity.</param>
    /// <exception cref="ArgumentException">The database path is blank or not absolute.</exception>
    public static void Configure(IServiceCollection services, string databasePath)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, HttpCurrentActor>();
        services.AddSingleton(new SqliteDatabase(databasePath));
        services.AddScoped<IVaultRepository, SqliteVaultRepository>();
        services.AddScoped<IAssetRegistrationStore, SqliteAssetRegistrationStore>();
        services.AddScoped<IAssetInspectionStore, SqliteAssetInspectionStore>();
        services.AddScoped<CreateVaultUseCase>();
        services.AddScoped<RegisterAssetUseCase>();
        services.AddScoped<InspectAssetUseCase>();
        services.AddScoped<IVaultArchiveStore, SqliteVaultArchiveStore>();
        services.AddScoped<ArchiveVaultUseCase>();
        services.AddScoped<IVaultMembershipStore, SqliteVaultMembershipStore>();
        services.AddScoped<VaultMembershipUseCases>();
        services.AddScoped<IOrdinaryAttributeStore, SqliteOrdinaryAttributeStore>();
        services.AddScoped<OrdinaryAttributeUseCases>();
        services.AddScoped<IEvidenceStore, SqliteEvidenceStore>();
        services.AddScoped<EvidenceUseCases>();
        services.AddScoped<IRelationshipStore, SqliteRelationshipStore>();
        services.AddScoped<RelationshipUseCases>();
    }

    /// <summary>Maps authenticated endpoints; use after AuthenticationHost.Map to retain antiforgery and safe failures.</summary>
    /// <param name="app">Configured application with scoped use cases.</param>
    public static void Map(WebApplication app)
    {
        MembershipApi.Map(app);
        AttributeApi.Map(app);
        EvidenceApi.Map(app);
        RelationshipApi.Map(app);
        var group = app.MapGroup("/api/v1").RequireAuthorization();
        group.MapPost("/vaults/{vaultId}/archive", async (string vaultId, HttpContext context, ArchiveVaultUseCase useCase, CancellationToken token) =>
        {
            // There is no business body; reject payloads rather than silently accepting identity fields.
            if (await context.Request.Body.ReadAsync(new byte[1], token) != 0) return ApiProblems.Result(400);
            if (!Guid.TryParseExact(vaultId, "D", out var id) || id == Guid.Empty) return ApiProblems.Result(400, "invalid_identity", "vaultId");
            return await useCase.ExecuteAsync(id, token) switch
            {
                ArchiveVaultOutcome.Archived => Results.NoContent(),
                ArchiveVaultOutcome.Unauthenticated => ApiProblems.Result(401),
                ArchiveVaultOutcome.InvalidIdentity => ApiProblems.Result(400, "invalid_identity", "vaultId"),
                ArchiveVaultOutcome.Unavailable => ApiProblems.Result(404),
                ArchiveVaultOutcome.Forbidden => ApiProblems.Result(403),
                _ => ApiProblems.Result(500)
            };
        });
        group.MapPost("/vaults", async (VaultInput input, CreateVaultUseCase useCase, CancellationToken token) =>
        {
            var type = input.Type switch
            {
                "personal" => VaultType.Personal,
                "household" => VaultType.Household,
                "organization" => VaultType.Organization,
                _ => (VaultType)(-1)
            };
            var result = await useCase.ExecuteAsync(new CreateVaultRequest(Guid.NewGuid(), input.Name, type), token);
            if (result.Vault is { } vault)
                return Results.Json(new VaultOutput { Id = vault.Id, Name = vault.Name, Type = input.Type! }, statusCode: 201);
            return result.Error switch
            {
                CreateVaultError.Unauthenticated => ApiProblems.Result(401),
                CreateVaultError.BlankName => ApiProblems.Result(400, "blank_name", "name"),
                CreateVaultError.InvalidType => ApiProblems.Result(400, "invalid_type", "type"),
                CreateVaultError.IdentityConflict => ApiProblems.Result(409, "identity_conflict"),
                _ => ApiProblems.Result(500)
            };
        });
        group.MapPost("/vaults/{vaultId}/assets", async (string vaultId, AssetInput input, RegisterAssetUseCase useCase, CancellationToken token) =>
        {
            if (!Guid.TryParseExact(vaultId, "D", out var id) || id == Guid.Empty) return ApiProblems.Result(400, "invalid_identity", "vaultId");
            var result = await useCase.ExecuteAsync(new RegisterAssetRequest(Guid.NewGuid(), id, input.Name), token);
            if (result.Asset is { } asset)
                return Results.Created($"/api/v1/assets/{asset.Id:D}", new AssetOutput { Id = asset.Id, VaultId = asset.VaultId, Name = asset.Name });
            return result.Error switch
            {
                RegisterAssetError.Unauthenticated => ApiProblems.Result(401),
                RegisterAssetError.BlankName => ApiProblems.Result(400, "blank_name", "name"),
                RegisterAssetError.VaultUnavailable => ApiProblems.Result(404),
                RegisterAssetError.Forbidden => ApiProblems.Result(403),
                RegisterAssetError.VaultArchived => ApiProblems.Result(409, "vault_archived"),
                RegisterAssetError.IdentityConflict => ApiProblems.Result(409, "identity_conflict"),
                _ => ApiProblems.Result(500)
            };
        });
        group.MapGet("/assets/{assetId}", async (string assetId, InspectAssetUseCase useCase, CancellationToken token) =>
        {
            if (!Guid.TryParseExact(assetId, "D", out var id) || id == Guid.Empty) return ApiProblems.Result(400, "invalid_identity", "assetId");
            var asset = await useCase.ExecuteAsync(id, token);
            return asset is null ? ApiProblems.Result(404) : Results.Ok(new AssetOutput { Id = asset.Id, VaultId = asset.VaultId, Name = asset.Name });
        });
        group.MapGet("/openapi.json", () => Results.Stream(
            typeof(RecordsApi).Assembly.GetManifestResourceStream("HomeVault.Api.homevault-v1.json")!, "application/json"));
    }
}
