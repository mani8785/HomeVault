# HV-22.4.5: Durable directed Relationships

Implements [#80](https://github.com/mani8785/HomeVault/issues/80) under
[ADR-0025](ADRs/0025-durable-directed-relationships.md), accepted 2026-10-04.
Parent #72/#26 remain open for remaining operation coverage.

## User behavior

Create an independently identified Covers Relationship from an insurance-policy
Asset to a covered Asset, inspect it, or mark it Removed. The source/target
meaning remains the caller's responsibility because Asset categories do not yet
exist. Both Assets must actually belong to the requested Vault; membership in
both Vaults does not permit a cross-Vault link. No reverse link is created.

Owner, Administrator and Editor may write in Active Vaults. Current members may
inspect Active or Removed roots, including Archived Vaults. Repeated removal
succeeds only after current permission and Active-Vault checks. Removal retains
the root and never deletes either Asset, attributes or Evidence. A removed id
stays reserved; a new id may recreate the same source/target/kind tuple. Reverse
direction is a separate tuple. Duplicate active tuples return 409.

The API exposes POST `/api/v1/vaults/{vaultId}/relationships` and GET/DELETE
`/api/v1/vaults/{vaultId}/relationships/{relationshipId}`. Creation accepts caller
id, sourceAssetId, targetAssetId and literal `covers`, returning 201 metadata and
Location. Inspection returns metadata only; removal returns empty 204. Missing/
inaccessible endpoints and wrong-Vault roots share 404. No list, graph traversal,
editing, reactivation, Asset deletion or new frontend is introduced.

## Implementation and database

RelationshipUseCases reads the trusted actor once and calls IRelationshipStore.
SqliteRelationshipStore starts a non-deferred write transaction before checking
membership, role, archive, actual endpoints and conflicts. Creation invokes
Relationship.Create; removal validates stored state with Relationship.Restore
and invokes Remove. Writes affect only the new row or its status. Read
transactions keep membership, root and actual endpoint ownership consistent.
All transport output is purpose-specific metadata; no Asset content is loaded.

Migration `20261004130707_AddRelationships` adds the Relationships table, restricted
Vault/source/target FKs, Domain-compatible checks and a unique index on
VaultId/SourceAssetId/TargetAssetId/Kind filtered to Active status. This preserves
Removed history while atomically preventing competing active duplicates. Same-Vault
ownership is enforced by the adapter, not the individual FKs. Direct external SQL
is unsupported, and future Asset movement needs a reviewed policy. Corrupt stored
ownership fails safely on inspection/removal.

No package or script was added. Existing EF Core/SQLite, ASP.NET authentication/
antiforgery, approved guards and project-owned Domain/results cover the operation.

## Validate from PowerShell

Use a clean working tree, run commands individually, and stop on failure:

```powershell
cd D:\repos\HomeVault
git fetch origin
git switch codex/hv-22-4-5-relationship-contract
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/relationships
dotnet ef migrations has-pending-model-changes --project src/HomeVault.Infrastructure --configuration Release --no-build
```

Tests cover restoration, trusted identity, all roles, same-Vault checks even for a
shared owner, missing/inaccessible equivalence, duplicates, reverse tuples,
retained removal, recreation and SQL uniqueness. Independent-connection tests
cover duplicate/removal/recreation and permission/archive contention, rollback,
invalid stored ownership, FKs and retained endpoints. Upgrade and verified backup/
restore preserve accounts, credentials, attributes, Evidence and Active/Removed
Relationships. HTTP tests use real accounts/cookies/SQLite, verify restart reads,
antiforgery, spoofing and access changes after authentication, and validate
responses against [OpenAPI 1.5.0](openapi/homevault-v1.json).

## Upgrade and run

First complete [account setup](hv-22-accounts.md), including private directories,
session keys and HTTPS certificate. Build successfully before using `--no-build`.
Stop the API and other writers, back up the existing account database to a new
path, then explicitly migrate; API startup never migrates automatically.

```powershell
$accountHome = "$env:LOCALAPPDATA/HomeVault-Accounts"
$accountDatabase = "$accountHome/accounts.db"
$accountKeys = "$accountHome/session-keys"
$backupPath = "$accountHome/before-relationships-$([Guid]::NewGuid().ToString('N')).db"
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage backup $accountDatabase $backupPath
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage migrate $accountDatabase
dotnet run --project src/HomeVault.Api -c Release --no-build -- serve $accountDatabase $accountKeys
```

The host listens at `https://localhost:7443`; stop with Ctrl+C. Recovery follows
the [verified restore procedure](hv-20-persistence.md) and
[account restore precautions](hv-22-accounts.md); retain a known-good original.

In a second terminal, follow the [authenticated journey](hv-22-authorized-api.md)
to obtain `$origin`, `$session`, refreshed `$csrf`, `$vault` and a fictional `$asset`.
Create a fictional insurance-policy Asset in that same Vault, then link it:

```powershell
$policy = Invoke-RestMethod "$origin/api/v1/vaults/$($vault.id)/assets" -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body '{"name":"Fictional insurance policy"}'
$relationships = "$origin/api/v1/vaults/$($vault.id)/relationships"
$relationshipId = [Guid]::NewGuid().ToString('D')
$body = @{ id = $relationshipId; sourceAssetId = $policy.id; targetAssetId = $asset.id; kind = 'covers' } | ConvertTo-Json
Invoke-WebRequest $relationships -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body $body
Invoke-RestMethod "$relationships/$relationshipId" -WebSession $session
Invoke-WebRequest "$relationships/$relationshipId" -Method Delete -WebSession $session -Headers @{'X-XSRF-TOKEN'=$csrf}
Invoke-RestMethod "$relationships/$relationshipId" -WebSession $session
```

Expected: 201 creation, `active` inspection, 204 removal, then `removed`
inspection with unchanged references. Repeating DELETE succeeds while authorized
and Active; neither Asset disappears. Use a new Relationship id to recreate the
association. Requests are not automatically retried. Keep real private metadata,
cookies and request bodies out of terminal transcripts and diagnostics.
