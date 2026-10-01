# ADR-0017: Durable local persistence and migration strategy

Status: Proposed
Created: 2026-10-01
Issue: [HV-20 / #24](https://github.com/mani8785/HomeVault/issues/24)

## Context and confirmed target

The owner selected local single-user Playground first, shared/server use later.
HV-19 already creates Vaults and registers Assets through storage-independent
Application contracts. Data currently disappears with the in-memory store.
The usage target is confirmed; the provider and ORM below await acceptance.

## Alternatives and recommendation

| Option | Fit and cost |
| --- | --- |
| SQLite with EF Core | Recommended for the first local version: no separate server; mapping and versioned schema migrations. SQLite-specific locking and schema limitations must be tested. |
| SQLite with direct Microsoft.Data.Sqlite | Equally viable local engine; explicit SQL gives precise control but requires more mapping and schema-version management code. |
| SQL Server with EF Core | A server-oriented alternative, historically mentioned but never accepted. Requires server provisioning and a different operational/test setup. Revisit for shared deployment. |
| Serialized files | Simple initial storage but requires custom atomic updates, indexing, concurrent access, referential integrity, and schema evolution. Not recommended for the accepted invariants. |

SQLite permits one writer at a time and discourages direct multi-client network
file access. This fits the confirmed first target, not a commitment to a future
server database. See [SQLite usage guidance](https://www.sqlite.org/whentouse.html).
EF Core does not make migrations/provider semantics interchangeable; changing
providers later needs a reviewed migration and data-transfer plan.

## Proposed boundaries and first schema

1. Use SQLite with EF Core in Infrastructure only. Keep Domain and Application
   free of provider types, DbContext, mapping attributes, and ORM dependencies.
   Map private persistence models rather than bypassing domain factories.
   Keep explicit composition and a short-lived context per operation.
2. Persist only currently supported creation data: Vault identity/name/type/state,
   memberships with unique (VaultId, ActorId), and Asset identity/VaultId/name.
   Use primary keys, required foreign keys, and appropriate enum/state constraints.
   Vault plus initial Owner insert together. Asset identities remain globally
   unique; unbound/prepopulated Assets remain rejected. No cascade delete API.
3. Preserve the existing insert-only contracts and safe outcome ordering.
   Translate only identified unique-identity violations into IdentityConflict;
   do not disguise foreign-key, corruption, I/O, or other provider failures.
4. Use a local explicitly configured database path outside the checkout and
   source artifacts. No network share, cloud-synced live file, credentials in
   source, or private user data in tests. First demonstrations use fictional data.
   Encryption/key management remains HV-21; this proposal does not claim it.
5. Select and pin compatible serviced EF Core/provider/tool versions during the
   first accepted implementation slice. Keep matching EF components on one
   version and validate against net10.0. No packages are installed by this ADR.

## Transactions and concurrency

Creation writes use database transactions. Registration must acquire SQLite's
write transaction before reading membership/role/state, then insert and commit
within that transaction. Use an immediate (non-deferred) transaction through
Microsoft.Data.Sqlite and enlist EF Core in it; verify the actual provider API
and behavior during implementation. A process-local lock is insufficient across
connections or processes. Future stored archive/member mutations must follow the
same transaction protocol. Merely checking a version on the inserted Asset does
not protect a concurrently changed Vault.

Use bounded lock waits and propagate exhaustion/cancellation without silently
retrying the business operation. A failed/interrupted response need not prove no
commit occurred. Test duplicates from separate connections and archive/access
state changes around competing registration. Preserve missing/inaccessible
equivalence and check access before identity conflicts. No public mutation APIs
are added solely to test this; controlled integration-test setup may alter state.

References: [SQLite transactions](https://learn.microsoft.com/dotnet/standard/data/sqlite/transactions),
[EF Core transactions](https://learn.microsoft.com/ef/core/saving/transactions),
[concurrency handling](https://learn.microsoft.com/ef/core/saving/concurrency).

## Schema evolution and rollback

Use reviewed, version-controlled EF migration classes and snapshots in
Infrastructure. Do not mix EnsureCreated with a migration-managed database.
Apply migrations explicitly through documented terminal tooling while the
Playground is stopped; never auto-migrate during normal application startup.
Review generated changes for data loss and test both empty-database creation
and upgrades containing fictional prior-version data. Back up before upgrades.

Prefer a forward corrective migration. For destructive or incompatible changes,
restore the verified pre-upgrade backup with a compatible application version;
do not promise arbitrary lossless Down migrations. Refuse unsupported schema
versions with safe diagnostics. SQLite schema rebuild and migration limitations
need review for every change. No generated SQL/helper scripts are authorized by
this proposal; the repository's separate script-approval policy still applies.

References: [SQLite provider limitations](https://learn.microsoft.com/ef/core/providers/sqlite/limitations),
[applying migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying),
[multiple providers](https://learn.microsoft.com/ef/core/managing-schemas/migrations/providers).

## Backup and restore

For the first local version, require an explicit backup before every schema
upgrade and provide a documented manual backup procedure. Use SQLite's supported
backup API, not an uncoordinated copy of a live database file. Store backups
outside the repository, protecting them like the database itself.

Restore into a separate location with the application stopped; verify integrity,
foreign keys, schema compatibility, and expected fictional records before
switching the configured path. Never overwrite the only known-good copy.
Test a full backup/restore round trip. Automatic schedules, retention policy,
encryption, and production recovery objectives require later operational review;
manual backup is the explicitly limited first-version proposal.

Reference: [Microsoft.Data.Sqlite backup API](https://learn.microsoft.com/dotnet/standard/data/sqlite/backup).

## Integration testing and observable reload

Use real temporary SQLite files, separate connections/contexts, and the actual
migrations in Infrastructure tests. Do not substitute EF's in-memory provider
for relational constraints or concurrency checks. Keep current in-memory tests
and run equivalent supported contract cases against the durable adapter.

Verify fresh-process save/reload, name/Guid preservation, atomic initial Owner,
no partial failures, foreign keys, cross-Vault identity conflicts, role/archive
checks, inaccessible/missing equivalence, snapshot independence, cancellation,
concurrent connections, migration upgrades, and backup restoration. Tests must
isolate their own temporary paths and never touch a configured personal database.

The current API has no reload operation. Before implementation, refine a narrow
actor-scoped Asset inspection use case returning immutable id/VaultId/name, with
one unavailable outcome for missing/inaccessible records. Authorized members,
including Viewers, may inspect archived Vault records under ADR-0004. Avoid
GetAll or a generic CRUD interface. Demonstrate this read in a second Playground
process to prove durability, not merely reuse tracked objects in one context.

Reference: [EF Core testing strategy](https://learn.microsoft.com/ef/core/testing/choosing-a-testing-strategy).

## Acceptance and subsequent scope

This PR delivers the decision proposal only and references, rather than closes,
HV-20. After explicit acceptance, create concrete implementation slices for:
schema/tooling and migrations; durable contract adapters and concurrency tests;
actor-scoped inspection and two-process Playground; backup/restore validation.
These are candidate work areas, not created or authorized implementation tasks.
Each slice needs concrete criteria and review; no database deployment, new
script, server hosting, or production authentication is implied.

## Confirmation

Only the local single-user usage target is confirmed. SQLite, EF Core, the
transaction protocol, migration/backup policy, and reload scope remain Proposed.
