# ADR-0024: Durable URL and Note Evidence

Status: Accepted
Created: 2026-10-03
Accepted: 2026-10-03
Issue: [HV-22.4.4 / #79](https://github.com/mani8785/HomeVault/issues/79)

## Context

Ordinary attributes (#78) are merged through PR #85. Evidence has Domain
add/remove operations and immutable snapshots, but no durable application or HTTP
boundary. [ADR-0022](0022-remaining-authorized-operations.md) requires review of
its exact API and schema before implementation. This proposal preserves
[ADR-0010](0010-asset-evidence.md), including deliberate content access.

## Proposed contract

### 1. Separate metadata inspection from deliberate content reads

All routes are under `/api/v1/assets/{assetId}/evidence`:

| Method and suffix | Input | Success |
| --- | --- | --- |
| POST (no suffix) | JSON `id`, `label`, `kind`, `content` | 201 with metadata and Location |
| GET (no suffix) | No body | 200 with `evidence` metadata array |
| GET `/{evidenceId}/content` | No body | 200 with JSON `content` |
| DELETE `/{evidenceId}` | No body | 204 |

Metadata contains only `id`, `label`, `kind`; it never contains content. Location
points to the content route, the individual read operation. Identifiers use
nonempty D-format Guids. The caller supplies the Evidence id, consistent with
ADR-0010; uniqueness is within the Asset. Reusing an id on a different Asset is
allowed. Duplicate addition returns 409 even if payloads match; it is not an
idempotency/retry mechanism. Removal permits later reuse under the existing Domain
contract; no tombstone/history is introduced. There is no edit or upsert route.

Kinds are exactly `url` and `note`. Preserve labels and content exactly. Notes
must be nonblank. URLs must satisfy existing Domain validation: absolute HTTP or
HTTPS, host required, no user-info, raw whitespace or control characters. Preserve
query strings/fragments and original spelling; never normalize or fetch them.
Equal label/content with different ids remains valid. No additional text limits
are introduced beyond the host's existing 16 KiB request limit. Metadata lists
have no ordering or pagination guarantee in this first local slice.

Require authenticated cookies and current membership for every read. Require
antiforgery and current Owner/Administrator/Editor on an Active Vault for writes.
Archived Vaults remain readable. Reject unknown JSON fields, caller-supplied
requester/Vault identity and DELETE bodies. Mark all responses no-store. Content
reads deliberately return JSON text, never HTML, redirects, downloads or fetched
resources. Future clients must render notes as text and make navigation explicit.

### 2. Plaintext storage boundary requiring explicit acceptance

Add `AssetEvidence` with a separate EF configuration and reviewed migration:

| Column | Storage rule |
| --- | --- |
| AssetId | Required TEXT Guid; FK to Assets.Id, restricted deletion |
| Id | Required TEXT Guid; CHECK nonempty Guid |
| Label | Required TEXT; nonblank and unchanged under Domain validation |
| Kind | Required INTEGER; CHECK IN (0, 1), matching Url/Note |
| Content | Required TEXT; validated content, preserved exactly |

Composite primary key `(AssetId, Id)` enforces Asset-local identity. Domain
restoration validates all remaining invariants; no uniqueness by label or content.
The additive migration creates an empty table and preserves existing attributes,
Assets, Vaults, memberships, accounts and credentials. Retain explicit operator
migration, schema-history checks and verified backup/restore. No startup migration.

**URL/Note content will be plaintext in SQLite and backups.** Deliberate method
access and membership checks do not encrypt data. This Evidence model has no
Sensitive classification, and URLs may contain private query parameters. This
proposal does not treat them as encrypted attributes or silently claim HV-21
coverage. Use this slice for non-secret supporting text/references; confidential
Evidence storage needs its own reviewed encryption policy. If plaintext Evidence
is unacceptable, defer persistence until that policy is approved instead.

### 3. Atomic operations and partial restoration

Application owns narrow add/remove/list-metadata/read-content contracts and safe
outcomes. Read ICurrentActor once per invocation. Resolve the actual Asset Vault
from storage and current membership; never authorize using a supplied Vault id.

Infrastructure acquires a non-deferred write transaction before access, role and
lifecycle checks. Restore the Asset identity and complete Evidence collection
through an explicit validated Domain factory, invoke existing AddEvidence or
RemoveEvidence, and persist only the affected row before commit. Invalid ids,
kinds, duplicate ids, labels, content or URLs fail restoration safely. Do not load
attributes or save an incomplete whole aggregate; ordinary/Sensitive attribute
state is neither inspected nor overwritten. Domain receives no EF row types,
mapping annotations, public mutation setters or creation events.

Metadata reads project only id/label/kind through a membership-scoped query; they
must not materialize Content. Deliberate content reads check membership and load
only the identified entry in one consistent read transaction, validate it, then
invoke ReadContent() and create a purpose-specific response. Missing Evidence
and missing/inaccessible Asset share the same unavailable result. No global
Evidence lookup or permission decision cached across requests is introduced.

### 4. Failure precedence and diagnostics

Authentication/antiforgery and transport shape/Guid parsing precede storage.
Well-formed writes then check Asset access (404 `unavailable`), role (403
`forbidden`), archive (409 `vault_archived`) and stored state. Domain validation
retains its identity, label, kind, content, URL-format, duplicate order. Invalid
input returns 400 with `invalid_identity`, `blank_label`, `invalid_kind`,
`blank_content` or `invalid_url`; duplicate id returns 409 `evidence_exists`.
Removing a missing entry returns 404 `unavailable`. Unexpected stored/provider
failure uses the existing safe 500 response. Malformed transport may fail earlier
without revealing record existence.

Never include labels, content, query strings or provider exception text in
diagnostics or failure payloads. Keep EF sensitive-data logging disabled and
avoid logging request/response bodies. No automatic business retries or claim
that an unsuccessful HTTP response proves no commit occurred.

## Alternatives and consequences

- Including content in every list response is simpler but undermines deliberate
  access. Separate metadata and content endpoints make disclosure explicit and
  avoid loading private notes merely to display a list.
- Server-generated ids are convenient but would change the accepted caller-id
  contract. Asset-local caller ids preserve Domain behavior and database uniqueness.
- Reusing the attribute table would conflate different validation and identity
  rules. A separate Asset-owned Evidence table avoids that coupling.
- Encrypting all Evidence now requires key/recovery and sensitivity decisions
  outside #79. Plaintext is a concrete limitation for review, not an encryption
  implementation or assurance that a URL contains no secrets.
- Existing EF Core, SQLite, ASP.NET authentication/antiforgery and approved
  Ardalis.GuardClauses are sufficient. No new package, script, generic repository,
  URL client, upload handler or file/document resolver is proposed.

## Implementation and validation after acceptance

1. Add explicit Evidence restoration and narrow Application contracts. Test
   validation precedence, safe failures, trusted identity and immutable snapshots.
2. Add mapping/migration and SQLite adapters. Test same-id isolation between
   Assets, concurrent duplicates, deletion, archive/permission races, rollback,
   malformed stored state and preservation of unrelated attributes.
3. Add HTTP DTOs/routes and OpenAPI. Test real accounts/cookies, all roles,
   missing/nonmember equivalence, spoofing, antiforgery, metadata redaction,
   deliberate content, invalid URLs, current membership and restart behavior.
4. Verify populated-schema upgrade and backup/restore preserve all existing
   data plus Evidence. Document terminal backup, migration, startup and journey.
   Run restore, format verification, Release build, NUnit, pending-model check
   and current-revision Windows/Linux CI; review public XML comments.

## Confirmation

The owner explicitly confirmed the routes, schema and plaintext limitation on
2026-10-03 and authorized implementation. Close #79 only after its implementation
merges; keep #72 and #26 open. File uploads, document resolution, encryption, UI
and subsequent Relationship/Reminder slices remain outside this step.
