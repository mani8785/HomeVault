# ADR-0026: Durable Reminder lifecycle

Status: Accepted
Created: 2026-10-04
Accepted: 2026-10-04
Issue: [#81](https://github.com/mani8785/HomeVault/issues/81), under #72/#26

## Context

[ADR-0011](0011-asset-reminders.md) accepts the Reminder domain lifecycle and
full-range UTC instants. [ADR-0022](0022-remaining-authorized-operations.md)
requires review of each durable operation's exact schema and HTTP contract.
Relationships are merged through PR #87; Reminders are the next bounded slice.

## Proposed HTTP contract

Base route: `/api/v1/vaults/{vaultId}/reminders`.

| Method and suffix | Input | Success |
| --- | --- | --- |
| POST base | id, assetId, action, dueAt | 201 metadata and Location |
| GET /{reminderId} | None | 200 metadata |
| GET /{reminderId}/action | None | 200 deliberate action DTO |
| PUT /{reminderId} | action, dueAt | 204 |
| POST /{reminderId}/complete | No body | 204 |
| POST /{reminderId}/cancel | No body | 204 |

Metadata contains id, vaultId, assetId, dueAt and status (pending, completed,
cancelled), never action text. The deliberate action DTO contains only action.
Creation uses a caller-supplied globally unique nonempty D-format Guid. Identity
and references cannot change; terminal roots and their identities remain retained.
Multiple Reminders for the same Asset/action/time are permitted.

dueAt is a required ISO 8601 timestamp with explicit Z or numeric offset, seconds,
and zero to seven fractional second digits. Reject missing, null, offsetless,
date-only, malformed and unrepresentable instants with 400 invalid_due_at; never
infer the machine timezone or silently truncate sub-tick precision. Normalize to
UTC and emit seven fractional digits with Z, preserving 100 ns precision and the
entire DateTimeOffset UTC range. Past dates and year 0001 are valid.

Authentication, current cookie identity, antiforgery on writes, unknown-field
rejection, the existing 16 KiB request limit and no-store responses apply. There
is no caller actor field. Action is nonblank and preserved exactly, with no new
business length limit. The transport body limit is not a Domain length invariant.

## Authorization, precedence and concurrency

Current members may read metadata and deliberately read action in Active or
Archived Vaults. Owner/Administrator/Editor may write only in Active Vaults,
including repeated terminal requests. Viewer receives 403; nonmembers and missing
Vaults share 404 unavailable. Actual stored Asset ownership must match the route
Vault, even if the requester belongs to both Vaults.

Transport shape, identity and timestamp parsing precede Application calls. Inside
the transaction check current membership, writer role, then archive state before
looking up the Asset or Reminder. Creation checks actual Asset ownership, Domain
action validation, then identity conflict. Blank action yields 400 blank_action;
same-Vault identity reuse yields 409 identity_conflict; a foreign-Vault identity
or missing/foreign Asset yields 404 unavailable without revealing ownership.

For updates/terminal operations, load the route-scoped Reminder and validate its
stored state and actual Asset ownership before invoking Domain behavior. Missing
or wrong-Vault Reminder yields 404. Corrupt stored state/ownership fails with a
safe 500, without content or identifiers in error diagnostics.

Update replaces action and dueAt atomically while Pending. A terminal Reminder
returns 409 not_pending before business action validation. Complete/Cancel move
Pending to the requested state; same-terminal repeats succeed unchanged; switching
terminal states returns 409 not_pending. Competing complete/cancel requests serialize:
one transition wins and the other conflicts. Concurrent Pending updates serialize
with the last committed update winning; no ETag or optimistic edit version is added.
Permission/archive changes and mutations serialize within a non-deferred SQLite
write transaction. Reads use a consistent transaction. No automatic business retry.

## Proposed schema and implementation

Add Reminders with Id TEXT primary key, VaultId TEXT, AssetId TEXT, Action TEXT,
DueAtUtcTicks INTEGER and Status INTEGER, all NOT NULL. Use existing Guid storage
conventions, a nonempty Id check, Status in (0,1,2), and integer ticks in
0..3155378975999999999. DueAtUtcTicks stores UTC .NET ticks, avoiding floating-point
loss and SQLite DateTimeOffset comparison limitations. Validate nonblank Action
through Domain on creation/restoration; SQL trim does not reproduce .NET whitespace.

VaultId and AssetId have separate restricted-delete foreign keys and indexes.
They ensure existence; the adapter enforces actual same-Vault ownership on every
operation. Do not alter Asset keys or introduce graph loading. Direct external SQL
and Asset movement remain unsupported; a future movement feature needs a policy.

Action is plaintext in the local database and backups, just like ordinary Note
Evidence. Separate reads and safe diagnostics prevent accidental disclosure, not
encryption. This slice does not claim Sensitive protection or implement HV-21.

Use narrow Application contracts with trusted ICurrentActor and safe results;
Infrastructure owns EF rows/configuration, transactions and UTC conversion. Add
validated Reminder restoration without replaying mutations or introducing EF into
Domain. Use the existing EF migration tools for an additive migration; preserve
all previous data and require explicit operator migration after backup. No startup
migration, new package, script, scheduler, notification, recurrence, listing,
overdue endpoint, reopening, deletion or frontend is included.

## Alternatives and consequences

ISO text could preserve precision but needs a fixed canonical format for ordering;
integer UTC ticks give exact range and order using existing .NET/SQLite types.
Unix milliseconds lose precision. A timezone library is unnecessary for fixed
instants; named-zone recurring schedules remain a separate decision.

Returning action with metadata is simpler but weakens deliberate private-content
access. Encryption would require the parked HV-21 key lifecycle and recovery work.
Existing EF Core, ASP.NET authentication/antiforgery, approved guards and Domain
results suffice; a scheduling package would solve a different requirement.

## Implementation and validation after acceptance

1. Add restoration, narrow Application operations and safe metadata/action DTOs.
2. Add mapping, migration and transactional adapter with actual ownership checks.
3. Add authenticated routes and update OpenAPI plus PowerShell usage documentation.
4. Test all roles, Archived reads/denied writes, current permission changes,
   isolation, antiforgery, safe outputs and restart using real cookies and SQLite.
5. Test offset normalization, min/max instants, fractional ticks, invalid timestamps,
   unchanged failures, competing updates/terminal transitions, rollback, constraints,
   upgrade preservation and verified backup/restore with all lifecycle states.
6. Restore, format-check, Release build, NUnit and verify current Windows/Linux CI.

Keep #72/#26 open: ordinary operations do not deliver Sensitive authorization or
encryption. Reconcile actual delivered coverage after Reminder implementation;
do not automatically close parents or resume parked UI work.

## Confirmation

The owner explicitly accepted this exact API, schema, plaintext action storage
and concurrency contract on 2026-10-04. Implementation of #81 is authorized.
