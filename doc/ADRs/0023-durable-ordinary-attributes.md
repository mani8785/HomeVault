# ADR-0023: Durable ordinary Asset attributes

Status: Proposed
Created: 2026-10-03
Issue: [HV-22.4.3 / #78](https://github.com/mani8785/HomeVault/issues/78)

## Context

Archive and membership are merged through PRs #83 and #84. Attributes currently
exist only in the Domain model. [ADR-0022](0022-remaining-authorized-operations.md)
requires review of each remaining operation's exact HTTP and storage contract.
This proposal supplies that gate for ordinary attributes; it does not implement
the routes or authorize Sensitive storage under the parked HV-21 work.

## Proposed decisions

### 1. Four operations, with names carried in request bodies

All routes operate on `/api/v1/assets/{assetId}/attributes`:

| Method and suffix | Request | Success |
| --- | --- | --- |
| POST `/add` | `name`, `value`, `sensitivity` | 204 |
| POST `/change` | `name`, `value` | 204 |
| POST `/remove` | `name` | 204 |
| GET (no suffix) | No body | 200 with an `attributes` array |

Use explicit command routes so arbitrary names, including slashes and Unicode,
do not become route segments or DELETE request bodies. Add requires the literal
classification `ordinary`; missing, unknown or `sensitive` classification is
rejected. Change cannot rename, upsert or reclassify. Reject unknown request
properties, including a caller-supplied requester identity. Retain existing
authentication, antiforgery and request-size controls. Return no value on writes.
GET returns only purpose-specific entries with `name`, `value` and
`sensitivity: "ordinary"`; an empty collection succeeds. No ordering or pagination
contract is introduced in this first slice.

Preserve [ADR-0006](0006-asset-attributes.md) and
[ADR-0007](0007-sensitive-attributes.md): trim names, match with
`StringComparer.OrdinalIgnoreCase`, retain original trimmed spelling, preserve
value whitespace, reject blank values and preserve immutable snapshots. Identical
replacement succeeds after authorization and lifecycle checks. Names remain
metadata and must not contain secrets. No new name/value length policy is added.

### 2. One ordinary-only table and explicit upgrade

Add `AssetAttributes` through a reviewed EF Core migration, with a separate
`IEntityTypeConfiguration` mapping following the existing persistence structure:

| Column | SQLite storage and rule |
| --- | --- |
| AssetId | Required TEXT Guid; FK to Assets.Id with restricted deletion |
| Name | Required TEXT, BINARY collation; trimmed original spelling |
| Value | Required TEXT; ordinary plaintext only |
| Sensitivity | Required INTEGER; CHECK Sensitivity = 0 (Ordinary) |

Use `(AssetId, Name)` as the composite primary key. Attributes acquire no separate
Domain identity. The binary key enforces exact duplicates; the adapter enforces
the stronger existing .NET ordinal-ignore-case rule while holding the write
transaction. Load the target Asset's attribute names and use the Domain comparer;
do not substitute a database collation or a homemade normalized key for that rule.
Validated restoration rejects logical duplicates, blank data and invalid stored
classifications. Direct external SQL writers are outside the supported mutation
boundary: this schema alone does not enforce all Domain invariants.

The upgrade creates an empty table and preserves existing Assets, memberships,
accounts and credentials. Continue explicit operator migration, migration-history
validation and verified SQLite backup/restore. Do not migrate automatically on
API startup or add a database package. Existing backups and ordinary values remain
plaintext. The stable encryption identity required by
[ADR-0018](0018-sensitive-value-encryption.md) remains a future reviewed migration;
this composite name key must not silently become encryption associated data.

### 3. Narrow atomic contracts and fail-closed reads

Application owns operation-specific add/change/remove/list contracts and safe
outcomes. Obtain the trusted requester from ICurrentActor once. Infrastructure
uses the actual Asset-to-Vault relationship, never a caller-supplied Vault, and
checks current membership. Owner, Administrator and Editor may write to Active
Vaults; Viewer may read. Archived Vaults remain readable.

Writes acquire the existing non-deferred SQLite transaction before checking
membership, role and lifecycle. Restore validated Asset identity and attribute
state through an explicit Domain factory using Domain-owned inputs, invoke the
existing behavior, then persist only the affected attribute row. Do not save a
whole aggregate or overwrite other Asset components. Restoration has no creation
events and does not claim to restore the still-unpersisted Evidence collection.
No storage entities, EF attributes or setters enter Domain.

Reads use one consistent SQLite read transaction for access, classification
preflight and projection. Before materializing any value, both reads and writes
inspect the target Asset's classification metadata. Unexpected non-ordinary
stored rows fail the entire operation safely without reading, disclosing,
overwriting or downgrading those values. The CHECK constraint is an additional
guard, not the only guard. Writes also constrain their affected row to Ordinary.
Unsupported future migration history is rejected by the existing schema check.
There is no deliberate Sensitive read API in this slice.

### 4. Failure precedence and safe responses

Authentication/antiforgery and transport-shape validation run first. For a
well-formed request, missing Asset and nonmember share 404 `unavailable` before
any attribute detail. Writers then check role (403 `forbidden`) and lifecycle
(409 `vault_archived`). Invalid stored state returns the existing safe unexpected
failure response without exception text or supplied data.

After access checks, mutations preserve Domain validation order: name, value
where applicable, classification on add, then duplicate/existence checks. Blank
name/value returns 400 `invalid_name`/`invalid_value`; unsupported classification
returns 400 `unsupported_sensitivity`; duplicate addition returns 409
`attribute_exists`; missing change/removal returns 404 `unavailable`. Structural
classification errors may be rejected earlier by transport validation. Never
include names, values, database exceptions or raw request bodies in diagnostics.
No automatic business retries or exactly-once HTTP guarantee is introduced.

## Alternatives and tradeoffs

- Resource routes containing names are conventional, but arbitrary existing names
  require careful escaping across clients and servers. Explicit commands keep
  this first API independent of route encoding without adding attribute IDs.
- A normalized key or custom SQLite collation could strengthen database-level
  logical uniqueness, but adds a second equality/versioning concern. Reuse the
  Domain comparer under the serialized write boundary for this local-first slice.
  Scanning attributes is proportional to the target Asset's attribute count;
  pagination and scale limits need a later concrete requirement.
- Generic repository save would risk unrelated state and separate permission
  checks from commit. Narrow operations retain the accepted atomic boundary.
- Ciphertext placeholders and nullable dual-purpose value columns would pre-empt
  the encryption design. An ordinary-only table deliberately requires an explicit
  upgrade before Sensitive persistence is possible.
- Existing EF Core/SQLite, ASP.NET authentication/antiforgery and approved
  Ardalis.GuardClauses suffice. No mapper, repository framework or new package is
  needed; Domain rules and safe result mapping remain project-owned.

## Implementation and evidence after approval

1. Add validated restoration and Application contracts/tests. Preserve Domain
   name matching, validation order, original spelling and snapshot behavior.
2. Add row mapping, migration and atomic SQLite adapters. Test Unicode/case
   collisions, concurrent adds, permission/archive races, rollback, cross-Vault
   isolation and preservation of unrelated attributes. Inject invalid stored
   classification in a controlled fixture to prove fail-closed behavior.
3. Add authenticated routes and versioned OpenAPI examples. Test real accounts,
   cookies, antiforgery, forged fields, all roles, revoked access and restart reads.
4. Test upgrade from the prior schema, existing data preservation and verified
   backup/restore. Update terminal instructions and XML documentation; run restore,
   format, Release build, NUnit, pending-model check and Windows/Linux PR CI.

This proposal does not complete #78. Keep #78, #72 and #26 open. Deliver the
implementation in its own review step after explicit acceptance of this ADR.

## Confirmation

Awaiting owner confirmation. No new API, table or migration is implemented by
this proposal. Existing accepted authorization and Domain decisions remain in force.
