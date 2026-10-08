# Authorized encrypted attributes (#65)

Owners and Administrators can add, change, remove and deliberately read Sensitive
attribute text. Editors and Viewers can list metadata but cannot access private
values. Current members may read archived Vaults according to those permissions;
archival blocks every write. See [ADR-0029](ADRs/0029-authorized-sensitive-attributes.md).

Sensitive text is encrypted before it reaches EF/SQLite. The new
`SensitiveAssetAttributes` table stores a stable Guid, actual Asset identity,
visible label, classification and authenticated binary envelope. Ordinary rows
remain in `AssetAttributes`. Both writers check names together in a serialized
transaction, preserving .NET ordinal-ignore-case matching across classifications.
Ordinary routes never decrypt or modify Sensitive rows.

## Validate from PowerShell

From a clean checkout of the PR branch, run each command separately and stop if
its exit code is nonzero:

```powershell
git switch codex/hv-21-3-sensitive-attributes
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/HomeVault.Infrastructure
```

The model check should report no changes. Tests use fictional text and isolated
temporary stores. Coverage includes role and archive checks, exact Unicode,
current membership, cross-classification uniqueness, record substitution,
tampering, missing keys, rollback, real journal/WAL bytes, EF command diagnostics,
populated schema upgrade, backup and actual Windows key-ring recovery. Absence
of plaintext sentinels is supporting evidence, not a proof of confidentiality.

## Explicit upgrade and host setup

Stop the host and all writers. Preserve a verified pre-upgrade database backup
using the [existing storage workflow](hv-20-persistence.md). Then explicitly apply
the additive migration to the chosen database (replace the fictional path):

```powershell
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- storage migrate 'C:\HomeVault\database\homevault.db'
```

Migration preserves existing rows and creates an empty encrypted table. Startup
never migrates. Automatic downgrade is blocked because it would discard encrypted
attributes; restore a verified pre-upgrade backup to a new location instead.
The existing account/session setup remains in the [account guide](hv-22-accounts.md).
Provision a data ring and private recovery-export directory using the
[key recovery guide](hv-21-key-recovery.md). All directories must be outside Git.

The database's containing directory, authentication-session directory, data ring
and export directory must be separate and nonoverlapping, with the documented
private Windows permissions. After a successful Release build, start the local
encrypted host in an interactive Windows terminal:

```powershell
dotnet run --project src/HomeVault.Api --configuration Release --no-build -- serve-encrypted 'C:\HomeVault\database\homevault.db' 'C:\HomeVault\sessions' 'C:\HomeVault\data-keys' 'D:\HomeVaultRecovery'
```

Enter the separately held random 64-hex-character recovery secret at the hidden
prompt. Never put it in an argument, environment variable, redirected input or
ordinary output. Successful startup publishes/verifies a fresh key and recovery
export before listening at `https://localhost:7443`. No schema or key provisioning
occurs implicitly. The existing `serve` command continues to operate without data
keys and does not expose Sensitive endpoints.

The host retains one write session, bounded to 65,536 nonce reservations; exhausted
writes return safe failure until an explicit restart/unlock publishes a fresh
session. Old keys remain readable. Shutdown clears owned key/secret buffers after
requests finish; managed strings and client memory cannot promise secure erasure.

## HTTP journey

Use the existing authenticated cookie and antiforgery terminal session from the
[authorized API guide](hv-22-authorized-api.md). Keep all example text fictional.
Requests are limited to 16 KiB; unknown fields are rejected. Operations below use
`/api/v1/assets/{assetId}/sensitive-attributes` as their base:

| Request | Body | Expected result |
| --- | --- | --- |
| POST `/add` | `{"name":"Private note","value":"fictional value","sensitivity":"sensitive"}` | 201, generated `id` |
| GET base | None | Metadata only: `id`, `name`, `sensitivity` |
| POST `/{attributeId}/read` | Empty | 200, deliberately requested `value` |
| POST `/{attributeId}/change` | `{"value":"replacement fictional value"}` | 204, same identity/name |
| POST `/{attributeId}/remove` | Empty | 204 |

Every POST, including read, requires the existing antiforgery header/cookies.
Responses use `Cache-Control: no-store`. Do not log returned values or request
bodies. There is no rename, reclassification, plaintext bulk listing or automatic
retry. Removing and re-adding a label creates a fresh identity.

Missing Assets/nonmembership return the same 404. Members without Sensitive
permission receive 403 for value operations. Archived writes return 409 after
role checks. Missing keys, exhausted sessions and invalid authenticated payloads
return the same 503 `sensitive_unavailable`, without provider details. Invalid
metadata fails safely. Authorized replacement/removal does not decrypt old text,
so damaged data can be deliberately replaced or removed.

## Recovery and remaining scope

The integration tests recover a separately exported Windows ring and authenticate
records in a copied SQLite database. This is not an operator workflow for publishing
a restored database configuration. #66 still owns coordinated backup/restore,
rotation, retention, interrupted recovery and old-backup rehearsals. Use fictional
Sensitive values until those requirements pass. Metadata, Ordinary attributes,
Evidence and Reminder text retain their existing visible/plaintext treatment.

No new NuGet package or cryptographic primitive is introduced. Framework AES-GCM,
the existing DPAPI custody, EF Core, SQLite and ASP.NET Core security are reused.
The console reader is shared as linked source between the two composition roots;
Domain/Application have no UI, crypto, Identity or EF dependency.
