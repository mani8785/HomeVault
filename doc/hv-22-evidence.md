# HV-22.4.4: Durable URL and Note Evidence

Implements [#79](https://github.com/mani8785/HomeVault/issues/79) under
[ADR-0024](ADRs/0024-durable-url-note-evidence.md), accepted 2026-10-03.
Parent #72/#26 remain open for the remaining operation slices.

## What users can do

Add a supporting URL or note to an Asset, inspect its Evidence metadata, deliberately
read one entry's content, and remove an entry. Metadata contains only id, label
and kind; neither creation responses nor lists disclose content. The separate
content endpoint returns JSON text with no-store caching, not a redirect or HTML.
No URL is fetched, and removing Evidence never deletes an external resource.

Current Vault members may read, including Archived Vaults. Owner, Administrator
and Editor may write only in Active Vaults. Current access is checked at execution;
the requester always comes from the validated session. Antiforgery protects writes.

The caller supplies a nonempty Guid, unique within that Asset. Same id in another
Asset is permitted; duplicate add conflicts, missing removal fails, and removed
ids may be reused. Labels and content retain their original Unicode and whitespace.
URL validation follows the existing Domain rules, including no raw whitespace,
user-info or non-HTTP/HTTPS scheme. A valid URL is not a claim that it is safe to visit.

Content is **plaintext in SQLite and backups**. Evidence has no Sensitive
classification or encryption in this slice. Use non-secret supporting references
and notes. Confidential Evidence, uploads, file paths and document resolution
remain separate design work. Default Domain/Application serialization omits content;
deliberate authorized reads do not revoke earlier snapshots.

## How it is implemented

EvidenceUseCases reads ICurrentActor once and delegates to IEvidenceStore.
SqliteEvidenceStore resolves actual Asset ownership, serializes writes with
membership and archive changes, restores validated Domain Evidence and persists
only the affected row. No whole-Asset save can overwrite attributes. Metadata
queries project only id/label/kind; content reads validate one entry inside a
consistent membership-scoped read transaction before calling ReadContent().

Migration `20261003124608_AddAssetEvidence` adds AssetEvidence with composite
AssetId/Id key, restricted Asset FK and checks for nonempty id and Url/Note kind.
Separate EF mapping keeps schema concerns out of Domain. Upgrade preserves
attributes, Assets, Vaults, memberships, accounts and credentials. Existing EF,
SQLite, ASP.NET authentication and approved guards suffice; no package or script
was added.

## Terminal validation

Use a clean checkout and run each command separately, stopping on failure:

```powershell
cd D:\repos\HomeVault
git fetch origin
git switch codex/hv-22-4-4-evidence-contract
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/evidence
dotnet ef migrations has-pending-model-changes --project src/HomeVault.Infrastructure --configuration Release --no-build
```

Tests cover exact text, snapshots, redaction, validation precedence, all roles,
cross-Vault isolation, same-id isolation, duplicate/removal contention, permission/
archive races, database rollback, malformed stored state and safe HTTP errors.
A disposable database test renames the content column to prove metadata and
unauthorized reads do not load it. Upgrade and backup/restore tests preserve
earlier records plus new Evidence. HTTP tests use real accounts/cookies and SQLite,
verify restart reads and check responses against [OpenAPI 1.4.0](openapi/homevault-v1.json).

Local validation on 2026-10-03: restore, format verification and Release build
passed with zero warnings/errors. All 564 tests passed (249 Domain/Application,
226 Infrastructure, 89 API), none skipped. EF reported no pending model changes.
Current-revision Windows/Linux CI remains the PR delivery gate.

## Upgrade and start the API

Complete the existing [account setup](hv-22-accounts.md), including protected
directories, session keys and HTTPS certificate. Build successfully first. Stop
the API and other writers, back up the existing account database to a new path,
then explicitly migrate it. The API does not migrate automatically.

```powershell
$accountHome = "$env:LOCALAPPDATA/HomeVault-Accounts"
$accountDatabase = "$accountHome/accounts.db"
$accountKeys = "$accountHome/session-keys"
$backupPath = "$accountHome/before-evidence-$([Guid]::NewGuid().ToString('N')).db"
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage backup $accountDatabase $backupPath
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage migrate $accountDatabase
dotnet run --project src/HomeVault.Api -c Release --no-build -- serve $accountDatabase $accountKeys
```

The host listens at `https://localhost:7443`; stop with Ctrl+C. For recovery, use
the [verified restore procedure](hv-20-persistence.md) and
[account restore precautions](hv-22-accounts.md), retaining the known-good original.

## Exercise Evidence from another terminal

Follow the [authenticated API journey](hv-22-authorized-api.md) to obtain `$origin`,
`$session`, refreshed `$csrf` and a fictional `$asset`. Use only fictional content
in these terminal examples; avoid transcript/body diagnostics for private data.

```powershell
$evidence = "$origin/api/v1/assets/$($asset.id)/evidence"
$evidenceId = [Guid]::NewGuid().ToString('D')
$body = @{ id = $evidenceId; label = 'Fictional receipt'; kind = 'url'; content = 'https://example.invalid/receipt?sample=1#details' } | ConvertTo-Json
Invoke-WebRequest $evidence -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body $body
Invoke-RestMethod $evidence -WebSession $session
Invoke-RestMethod "$evidence/$evidenceId/content" -WebSession $session
Invoke-WebRequest "$evidence/$evidenceId" -Method Delete -WebSession $session -Headers @{'X-XSRF-TOKEN'=$csrf}
Invoke-RestMethod $evidence -WebSession $session
```

Expected: creation returns 201 with metadata and a content-read Location; listing
omits content; deliberate reading returns the original URL without fetching it;
deletion returns 204; final list is empty if no other Evidence exists. For a note,
use a new id, `kind = 'note'` and nonblank fictional text. Repeating the same add
before deletion returns 409; missing entries and inaccessible Assets share 404.
Viewer writes return 403 and otherwise-authorized archived writes return 409.
No frontend is added by this change.
