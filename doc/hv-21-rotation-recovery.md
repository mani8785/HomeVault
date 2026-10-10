# Offline encryption rotation and coordinated recovery (#66)

[ADR-0030](ADRs/0030-offline-rotation-coordinated-recovery.md) was accepted on
2026-10-09. These Windows operator commands rotate Sensitive envelopes, create a
verified database/key recovery set, and recover into a new location. They do not
start a server or replace a live database. Ordinary values and metadata remain
visible. The separate random recovery secret must never be stored in the set.

## Build and test from PowerShell

From the repository root, run each command separately; continue only on exit 0:

```powershell
git switch main
git pull --ff-only
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
```

Windows runs custody, interruption, recovery and authorization integration tests.
The disposable second-profile recovery rehearsal runs in Windows CI; local tests
do not create another account. Linux exercises the manifest, rotation core and
explicit unsupported-platform results. No manual maintenance is necessary to run
the tests: they use isolated fictional stores.

## Preconditions

Stop the API, Playground and every SQLite editor or other writer. Work in an
interactive Windows terminal without redirected input/output or a transcript.
Use the same Windows account that owns the existing database and ring. Keep the
known-good database, ring, exports and earlier backups. Verify these operations
with fictional data first. The [completion record](hv-21-encryption-completion.md)
documents #66's completed checks and reviewed merge; a successful build alone
does not replace correct provisioning and a recovery rehearsal.

The examples below use nonsecret placeholder paths; replace them with your actual
absolute paths. All directories must be outside Git checkouts, without reparse
points, and private to the current user. Data, session keys, data keys and external
exports must occupy separate, non-overlapping directories. New destinations must
not exist, and their parent must exist. See [private host storage](hv-21-sensitive-attributes.md)
and [key provisioning](hv-21-key-recovery.md).

Each command prompts for the existing random 32-byte recovery secret, entered as
64 hidden hexadecimal characters. It is never an argument or environment variable.
Success returns exit 0; failure returns exit 1 with a fixed safe message.

## Rotate existing Sensitive values

After a successful Release build:

```powershell
dotnet run --project src/HomeVault.Api --configuration Release --no-build -- rotate-encryption 'C:\HomeVault\database\homevault.sqlite' 'C:\HomeVault\data-keys' 'D:\HomeVaultExports'
$LASTEXITCODE
```

The command holds the same database exclusion lease as the API and then the ring
writer lock. It validates schema, integrity, account references, Vault invariants,
attribute metadata and decryption before rotation. It publishes and verifies a
fresh key/export before dependent writes, then commits at most 128 envelopes per
transaction. A new session is published at the 65,536-write bound. Archived Vaults
are included; names, values, identities, memberships and archive state do not change.

On interruption, committed batches remain readable alongside older generations.
The current batch rolls back. Rerun the same command after resolving the cause;
it scans all records again using fresh keys. It does not reactivate an old write
key or promise to skip earlier work. Missing/corrupt records fail closed.

## Create a coordinated recovery set

```powershell
dotnet run --project src/HomeVault.Api --configuration Release --no-build -- backup-encrypted 'C:\HomeVault\database\homevault.sqlite' 'C:\HomeVault\data-keys' 'D:\HomeVaultBackups\set-001'
$LASTEXITCODE
```

Keep the resulting directory as one unit:

| File | Contents |
| --- | --- |
| `database.sqlite` | SQLite backup with ciphertext Sensitive values and visible ordinary data/metadata |
| `keys.hvkr` | Authenticated recovery export matching the current ring |
| `manifest.hvbm` | Authenticated binding of both files, their lengths/hashes and the ring generation |

No DPAPI blob or session-cookie key is included. Full validation and read-back
verification precede publication by a same-volume directory rename. Do not add
SQLite WAL, SHM or journal sidecars: recovery rejects them. Opening the backup in
a writable SQLite viewer can alter its bytes and invalidate its manifest.

Backup does not need a new write key. Every included Sensitive record must be
readable with the included export. A checksum alone is insufficient: the manifest
is authenticated using a separately derived key from the recovery secret.

## Recover, verify access and activate deliberately

Transfer the entire set to the replacement Windows profile through a trusted
channel. The imported directory and three files must be owned by the destination
user with protected ACLs granting only that user access. File copying does not
guarantee these permissions; use Windows security properties to establish them.
The command rejects unsafe permissions rather than repairing existing files.

```powershell
dotnet run --project src/HomeVault.Api --configuration Release --no-build -- recover-encrypted 'D:\HomeVaultBackups\set-001' 'C:\HomeVaultRecovered'
$LASTEXITCODE
```

The destination contains `database\homevault.sqlite`, `data-keys` and
`session-keys` as separate private stores. Recovery authenticates the source and
staged bytes before opening SQLite, rewraps data keys for the destination Windows
profile, validates every Sensitive record and current-schema authorization
structure, and creates fresh session keys. Account security stamps change and
outstanding invitation/recovery credentials become consumed. Passwords, disabled
states and memberships are preserved; old cookies and credentials cannot be reused.

The result is a live-store layout, not another signed backup: session invalidation
changes the database, so the source manifest is not retained in that layout.
The source backup remains untouched. Only the current exact schema is supported;
an older backup from the same schema remains usable after rotation. Older-schema
backups need an explicitly verified migration workflow before this command applies.

Review the historical accounts and memberships before activation. A backup cannot
know about revocations made after it was taken, and recovery does not reenable a
disabled owner. No configuration file is switched automatically. Once verified,
use a separate existing private export directory and start the host explicitly:

```powershell
dotnet run --project src/HomeVault.Api --configuration Release --no-build -- serve-encrypted 'C:\HomeVaultRecovered\database\homevault.sqlite' 'C:\HomeVaultRecovered\session-keys' 'C:\HomeVaultRecovered\data-keys' 'D:\HomeVaultExports'
```

This requires the existing trusted development HTTPS certificate and prompts for
the recovery secret. Startup publishes a new write key/export before listening at
`https://localhost:7443`. Sign in again. Owner/Administrator text access, denied
Editor/Viewer text access and nonmember unavailability remain unchanged. See the
[Sensitive API guide](hv-21-sensitive-attributes.md) for requests and antiforgery.

## Failure and retention

An existing destination is never overwritten. Interrupted backup/recovery may
leave a private `<destination>.partial-<random-id>` directory for inspection;
it is not a completed set or a usable restored configuration. Preserve the source,
resolve the cause and retry with a new destination. Cleanup of abandoned staging
is a deliberate operator action; the command does not recursively delete paths.

Keep old keys, exports and backups. A successful rotation does not make old keys
unnecessary: previous backups still reference them. The 4,096-key capacity fails
closed; there is no automated deletion, capacity bypass or retirement command.
Retirement requires a separately reviewed inventory and recovery rehearsal.

Locks protect cooperating HomeVault entry points, not arbitrary tools. Publication
uses flushed files and directory rename, but does not promise filesystem-independent
power-loss durability. Recovery verification rejects incomplete/corrupt sets.
Managed decrypted strings cannot promise secure erasure; owned key/byte buffers
are cleared and plaintext is never exported, logged or sent to SQLite.

## Manifest version 1

All integer fields are big-endian; GUIDs use network byte order. Total size is
exactly 173 bytes; trailing data and unknown versions fail. Paths are fixed by
the command, never supplied by the manifest.

| Offset | Bytes | Field |
| --- | --- | --- |
| 0 | 4 | ASCII `HVBM` |
| 4 | 1 | Version `1` |
| 5 | 32 | Random HKDF salt |
| 37 | 16 | Nonempty ring ID |
| 53 | 8 | Unsigned ring generation |
| 61 | 8 | Database file length |
| 69 | 8 | Recovery-export file length |
| 77 | 32 | Database SHA-256 |
| 109 | 32 | Recovery-export SHA-256 |
| 141 | 32 | HMAC-SHA-256 of bytes 0–140 |

HKDF-SHA-256 derives 32 bytes from the random recovery secret with that salt and
UTF-8 purpose `HomeVault.BackupManifest.v1`. The tag is checked in constant time
before trusting fields; file hashes are streamed and checked before database use.
This authenticates the set, including its account metadata; it cannot detect
replay of an older valid set. Tests independently derive HKDF extract/expand and
mutate every manifest byte, lengths, file contents and file pairing.
