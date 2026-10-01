# Durable persistence implementation

Implements accepted [ADR-0017](ADRs/0017-durable-local-persistence.md) in separate
review slices. EF Core SQLite, Design, and local dotnet-ef are pinned to 10.0.12.
Domain and Application have no ORM references. Storage models stay internal to
Infrastructure. Names preserve domain validation semantics; the database enforces
identities, foreign keys, membership uniqueness, and valid enum states.

## Schema and migrations (#52)

InitialVaults creates Vaults and memberships; AddAssets adds Vault-bound Assets.
Tests upgrade a populated first schema and reopen it with the latest model.
Normal application operations never migrate automatically. SqliteDatabase's
explicit migration entry point refuses unknown/non-prefix histories.

## Durable adapters (#53)

SqliteVaultRepository and SqliteAssetRegistrationStore implement the existing
contracts with one context per call. Non-deferred SQLite transactions reserve
the write lock before duplicate or access checks. Existing records are never
overwritten. Registration checks membership, role, and archive state before
global identity conflicts; other database failures propagate. Foreign keys are
enabled on every connection. A five-second provider lock timeout bounds waits;
there is no automatic application retry. Cancellation is checked before opening
and after acquiring the write reservation; provider operations may block until
the bounded lock wait ends. Future state changes must use compatible transactions.

## Inspection and cross-process verification (#54)

InspectAssetUseCase reads a trusted actor once and returns an immutable view or
one unavailable (null) result. SqliteAssetInspectionStore joins Asset and current
membership in one SQL query; all members, including Viewers, may read archived
Vaults. A prior result does not grant permission for a later read.

Use a fresh fictional database and run each command as a separate process after
a successful Release build. Create the parent directory first:

```powershell
$databasePath = "$env:LOCALAPPDATA/HomeVault/fresh-demo.db"
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- storage migrate $databasePath
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- storage create $databasePath
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- storage read $databasePath
```

Expect `Durable reload and nonmember isolation verified.` The CLI uses fixed
fictional identities; it is not an authentication provider or general user CLI.
Repeated create deliberately fails with nonzero exit instead of overwriting data.
Normal create/read never applies migrations. Errors omit database paths and values.

From the repository root:

```powershell
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/persistence
```

To apply migrations to a fresh fictional database, first create a dedicated local
directory outside the checkout and stop application writers. Before upgrading an
existing database, make and verify a supported backup; backup tooling follows #55.
Never use an uncoordinated live-file copy. Do not use a network/cloud-synced path.

```powershell
New-Item -ItemType Directory -Force "$env:LOCALAPPDATA/HomeVault"
$env:HOMEVAULT_DATABASE = "$env:LOCALAPPDATA/HomeVault/playground.db"
dotnet ef database update --project src/HomeVault.Infrastructure
```

The design-time factory defaults to an ephemeral in-memory database when no path
is supplied, so always set HOMEVAULT_DATABASE for an intended persistent update.
Review generated C# migration classes and model snapshots before committing.
No SQL/helper scripts are generated. Do not use EnsureCreated, automatic startup
migration, or destructive Down migration as a substitute for verified recovery.

Database paths and backup contents may be private. Do not log connections, enable
sensitive EF logging, commit database files, or put real secrets in demonstrations.
Encryption, production authentication, and hosting remain separate work.

## Backup, restore, and migration recovery (#55)

Stop application writers before migration or restore. The explicit backup/restore
commands use SQLite BackupDatabase with a read-only source. They stage a new
destination, verify integrity, foreign keys, and known schema history, then
publish without overwriting. An existing destination is rejected. Backup and
restore preserve the source; protect all files using appropriate OS permissions.

```powershell
$backupPath = "$env:LOCALAPPDATA/HomeVault/before-upgrade.db"
$restoredPath = "$env:LOCALAPPDATA/HomeVault/restored-demo.db"
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- storage backup $databasePath $backupPath
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- storage restore $backupPath $restoredPath
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- storage read $restoredPath
```

Use new destination names for every run. Verify the expected fictional records
and membership isolation before switching the configured path. For real data,
verify the expected records through an authorized read interface; the fixed-ID
demo command is not a general recovery auditor. Older recognized backups retain
their schema version. Use a compatible application, or explicitly migrate the
restored copy after preserving the original backup. Never downgrade the only
known-good database with Down migrations.

Tests rehearse backup/restore and recovery of a populated prior schema after an
upgrade. Cancellation, corrupt input, unknown schema, and destination collisions
must not publish an invalid copy. The backup API is synchronous; cancellation is
checked around it, not claimed to interrupt native copying. Automatic schedules,
retention, encryption/key recovery, and production recovery-time objectives are
not implemented. No personal database is used in tests.
