# HV-22.4.1: Owner-only durable Vault archival

Implements [#76](https://github.com/mani8785/HomeVault/issues/76) under accepted
[ADR-0022](ADRs/0022-remaining-authorized-operations.md). Parent #72 and story #26
remain open for the remaining operations. No schema migration or package is added.

## Contract and behavior

POST /api/v1/vaults/{vaultId}/archive requires a valid HomeVault session and
antiforgery state. It accepts no request body (even an empty JSON object is rejected).
The actor comes only from validated cookie identity; query/header actor fields
cannot override it. The route requires a non-empty Guid in D format.

| Condition | Response |
| --- | --- |
| Current Owner, Active or already Archived | 204, empty body |
| Current Administrator, Editor or Viewer, in either state | 403 |
| Missing Vault or nonmember | Identical safe 404 |
| Invalid/empty identity, body supplied, or invalid antiforgery | 400 |
| Missing/revoked session | 401 |
| Unexpected storage/invalid stored snapshot failure | Safe 500 |

Existing request size/rate limits and no-store responses apply. There is no
unarchive operation. Archive retains metadata, memberships and Assets; current
members can still read existing Assets, while registration now fails with the
existing archived outcome. Later operations must preserve that write barrier.

## Implementation

ArchiveVaultUseCase reads ICurrentActor once, checks cancellation and target
identity, then awaits IVaultArchiveStore. It does not authenticate, retry or expose
storage exceptions as business results. SqliteVaultArchiveStore starts an immediate
write transaction before current membership checks and holds it through commit.
It restores only the Vault and memberships, invokes Vault.Archive, and updates
Status. It never loads all Assets or rewrites membership records.

Vault.Restore copies a complete snapshot and validates identities, name, type,
state, roles, duplicate actors and at least one Owner. It restores Archived state
without replaying creation or membership mutation commands. Invalid stored data
causes a constant safe exception, not a partly valid domain object or a repair.
The HTTP boundary returns generic Problem Details without exception text.

SQLite serializes concurrent writers: registration committed before archival is
retained; registration ordered after archival is rejected. Membership changes
committed before the archive transaction are observed. Already committed work is
not retroactively revoked. A failed response does not prove that no commit occurred.

## Validation and terminal journey

From the repository root, run each command and stop on failure:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore -warnaserror
dotnet test -c Release --no-build --no-restore --logger trx --results-directory TestResults/vault-archive
dotnet ef migrations has-pending-model-changes --project src/HomeVault.Infrastructure --configuration Release --no-build
```

Tests cover restored snapshots, application identity/cancellation/outcome handling,
all roles, no partial writes, safe failures, competing SQLite connections, current
membership changes, real HTTP cookies/antiforgery, spoofed inputs and restart.
TestHost restart reuses fixture Data Protection keys; it is not a machine-restart
or browser compatibility test. Existing Windows key tests cover OS-specific keys.

Use the [account setup](hv-22-accounts.md) and
[authenticated API journey](hv-22-authorized-api.md) with fictional data. Start the
existing API using your already configured private database/key directories:

```powershell
dotnet run --project src/HomeVault.Api -c Release --no-build -- serve $accountDatabase $accountKeys
```

In the second terminal, after that guide has created an authenticated $session,
fresh $csrf, $origin and a fictional $vault/$asset, archive the test Vault:

```powershell
Invoke-WebRequest "$origin/api/v1/vaults/$($vault.id)/archive" -Method Post -WebSession $session -Headers @{'X-XSRF-TOKEN'=$csrf}
Invoke-RestMethod "$origin/api/v1/assets/$($asset.id)" -WebSession $session
```

Expect 204 followed by the retained fictional Asset metadata. Repeating archive as
the Owner returns 204; trying to create another Asset in that Vault returns 409.
This intentionally changes the chosen Vault irreversibly through this API, so use
the fictional test Vault. Stop the host with Ctrl+C. Membership management remains
in #77; there is no supported membership-changing HTTP command in this slice.
