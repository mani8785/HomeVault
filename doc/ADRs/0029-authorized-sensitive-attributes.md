# ADR-0029: Authorized encrypted Asset attributes

Status: Proposed
Created: 2026-10-05
Issue: [HV-21.3 / #65](https://github.com/mani8785/HomeVault/issues/65)

## Context and review boundary

PR #90 is merged. [ADR-0027](0027-encryption-envelope-key-lifecycle.md) and
[ADR-0028](0028-windows-key-custody-recovery.md) now have implemented envelope,
Windows custody and portable key recovery support. Neither enables Sensitive
database operations. [ADR-0018](0018-sensitive-value-encryption.md) explicitly
requires review of the operation, stable identity and migration before integration.
[ADR-0020](0020-local-accounts-vault-authorization.md) leaves Sensitive permission
separate from ordinary access. This proposal supplies those missing decisions.

This supplements ADR-0018/0020/0023. It preserves the ordinary-only table and
routes accepted in [ADR-0023](0023-durable-ordinary-attributes.md), but extends the
serialized name-uniqueness check to include Sensitive metadata. It does not grant
ordinary endpoints permission to read, overwrite, remove or downgrade Sensitive
values. No new package, database provider or authentication provider is proposed.

## 1. Explicit Sensitive permission policy

| Operation | Owner | Administrator | Editor | Viewer |
| --- | --- | --- | --- | --- |
| List Sensitive metadata | Yes | Yes | Yes | Yes |
| Deliberately read Sensitive text | Yes | Yes | No | No |
| Add/change/remove Sensitive attributes in an Active Vault | Yes | Yes | No | No |

Archived Vaults remain readable under the same role policy; all mutations remain
blocked. Query current membership and the actual Asset-to-Vault relationship on
every operation. A cookie contains identity, not authority to decrypt. Account
administration does not grant Vault access. Existing membership rules mean an
Owner can grant an Administrator access; no per-attribute ACL, reauthentication
prompt or new role is introduced in this slice.

Metadata is the stable attribute ID, trimmed name and classification only. It is
visible to current members and remains visible in copied databases. Never put a
secret in the name. Metadata queries do not select ciphertext or request a key.
Previously delivered plaintext cannot be revoked by later permission changes.

## 2. Narrow Application and HTTP operations

Application owns an ISensitiveAttributeStore boundary and purpose-specific use
cases. Capture the trusted ICurrentActor once, and keep crypto/provider/key types
inside Infrastructure. Use explicit output types: metadata has no value field;
deliberate read results redact normal formatting/serialization and expose text
only through a deliberate method used by the authorized HTTP mapping.

Proposed routes below are relative to
`/api/v1/assets/{assetId}/sensitive-attributes`:

| Method and suffix | Body | Success |
| --- | --- | --- |
| POST `/add` | `name`, `value`, `sensitivity: "sensitive"` | 201 with generated `id` only |
| POST `/{attributeId}/change` | `value` | 204 |
| POST `/{attributeId}/remove` | Empty | 204 |
| GET (no suffix) | None | 200 with `attributes: [{id, name, sensitivity}]` |
| POST `/{attributeId}/read` | Empty | 200 with `value` only |

The deliberate read uses POST and the existing antiforgery protection. All
Sensitive responses, including errors, carry `Cache-Control: no-store`. Keep
HTTPS, authenticated identity, enabled-account/security-stamp checks, request
rate limits, strict JSON shape handling and the existing 16 KiB HTTP body limit.
Reject actor IDs, supplied attribute IDs on add and unknown fields. No bulk
plaintext export, plaintext list, rename, upsert or classification conversion.

Reuse Domain rules: nonblank text, exact value whitespace, trimmed original name
spelling and OrdinalIgnoreCase name matching. Domain validation remains explicit
and framework-independent. Add a narrow validated candidate factory/name check if
needed so storage can enforce these rules without manufacturing placeholder
values or decrypting unrelated attributes. Do not restore a whole Asset merely
to modify one encrypted row. Infrastructure assigns a fresh nonempty Guid on a
successful add; changing text preserves it, and removal followed by re-addition
gets a new Guid. It is a durable attribute identity, not a new aggregate root or
permission to add a generic Entity base class.

## 3. Additive schema and Ordinary coexistence

Create `SensitiveAssetAttributes` with its own EF configuration and reviewed
migration. Leave the existing `AssetAttributes` rows/schema and Ordinary values
unchanged:

| Column | Rule |
| --- | --- |
| Id | Required TEXT Guid primary key, nonempty; server generated |
| AssetId | Required TEXT Guid FK to Assets.Id, restricted deletion |
| Name | Required TEXT, BINARY collation; trimmed original spelling |
| Sensitivity | Required INTEGER, CHECK = 1 |
| Envelope | Required BLOB containing the complete ADR-0027 envelope |

Add an exact unique index on `(AssetId, Name)`. Under the same non-deferred SQLite
write transaction, inspect names/classifications from both tables and enforce
OrdinalIgnoreCase uniqueness across them. Do not substitute SQLite NOCASE for
the Domain comparer. Reject invalid metadata or logical duplicates safely.
Direct external SQL writers remain outside the supported mutation boundary.

Ordinary add checks both sets of names. Ordinary list returns Ordinary entries
only; Ordinary change/remove never affect the Sensitive table. A name which
exists only as Sensitive is unavailable through Ordinary change/remove. Mixed
Assets continue to support ordinary operations without accessing data keys.
Do not load Sensitive envelopes or plaintext during those operations.

The new table avoids an automatic conversion of historical plaintext or a
nullable column that alternates between plaintext and ciphertext. Explicitly
migrate with the existing EF tooling; never migrate on startup. Upgrade and
backup-history tests must prove existing accounts, memberships, Assets and all
prior component rows are preserved. Older binaries must reject the newer history.
Document rollback as restoration of a verified pre-upgrade backup, rather than
silently dropping a populated encrypted table. No destructive downgrade workflow.

## 4. Transaction, crypto and failure boundaries

Resolve current access before selecting the envelope or requesting a key. Reads
hold one consistent SQLite read transaction for membership, actual ownership,
attribute identity and decryption. This is authorization at the read snapshot;
it cannot retroactively revoke an in-flight response.

Writes acquire the existing non-deferred transaction before current role/archive
checks. Validate metadata and the requested mutation, encrypt the supplied value
in memory, and give EF only the encrypted BLOB. Bind actual VaultId, AssetId and
the stable row Id as ADR-0027 associated data. Never use a mutable name as the
identity. Commit only the affected row; failed encryption or SQL leaves data
unchanged while consuming any reserved nonce. Changing/removing an authorized
row does not need to decrypt its old value; this allows deliberate replacement
or deletion of damaged data without a plaintext fallback.

For well-formed requests, missing Asset/nonmembership yields identical 404
`unavailable`, followed by role denial (403), then write archive denial (409
`vault_archived`). A missing/mismatched attribute is 404 after access checks.
Invalid name/value is 400; duplicate name is 409 `attribute_exists`. Preserve
existing Domain validation precedence where applicable. Unknown enum/stored
metadata produces a safe server failure. Invalid UTF-16 or an envelope size
violation is safe `invalid_value`; the HTTP size limit remains independently
enforced before Application input processing.

Missing keys, exhausted sessions, corrupt/versioned payloads and authentication
failures map to one 503 `sensitive_unavailable` after authorization. Do not expose
key IDs, provider errors, envelopes, request bodies or private text in diagnostics.
No EF automatic decryption/value converter or sensitive-data logging. Clear owned
temporary byte buffers; managed strings and client responses cannot promise
perfect erasure. Lists never decrypt values, including on error paths.

## 5. Explicit local host unlock

Add a Windows-only operator command:

`serve-encrypted <database> <session-key-directory> <data-ring-directory> <export-directory>`

It keeps the current localhost HTTPS binding and prompts once for the separately
held random recovery secret using ADR-0028 hidden input before listening. Accept
no secret argument, environment variable, redirected input or browser unlock
endpoint. Verify existing private artifacts and publish a fresh verified write
session before enabling Sensitive routes. Require database, session-key directory,
data ring and export directory to be separate, nonoverlapping configured locations
outside the checkout, with existing appropriate permission checks. No implicit
provisioning or migration. Missing/corrupt configuration fails startup safely.

The host owns one bounded write session for its lifetime, serializes its use and
clears owned custody/session secret buffers on shutdown. At its 65,536-reservation
limit, reject further Sensitive writes safely; an explicit restart/unlock creates
a fresh session and export. Retained keys remain read-only. No unattended unlock,
automatic secret persistence or background rotation is added.

Keep existing `serve <database> <session-key-directory>` available for ordinary
operations without unlocking data keys; it does not map Sensitive routes. Both
modes retain name-uniqueness protection on mixed Assets. Linux tests may use an
internal fictional custody seam; normal hosting never has a fake-key fallback.

## 6. Implementation sequence and acceptance evidence

After owner acceptance, complete these tasks on this branch and update its PR:

1. Domain candidate validation and narrow Application operations/results, with
   all four roles, archived reads and explicit sensitive permission tests.
2. Separate EF mapping/migration and transactional adapter. Test populated
   upgrades, mixed Ordinary/Sensitive names, no downgrade, rollback, exact Unicode
   and whitespace, fresh IDs after re-add, and preserved IDs after change.
3. Host unlock and authenticated HTTP routes/OpenAPI. Test real cookies,
   antiforgery, forged identities, revoked membership, no-cache responses,
   disabled accounts, independent-connection archive/permission races and restart.
4. Tamper with headers/payloads and swap envelopes between Vaults, Assets and
   attributes; test missing keys and safe errors without decrypting for denied
   actors. Exercise actual Windows custody with fictional data end to end.
5. Inspect EF-bound rows, raw database files, WAL/rollback journals and SQLite
   backups plus captured diagnostics for unique fictional plaintext sentinels,
   including failed writes. Arrange live journal/WAL coverage explicitly; a
   nonexistent file is not coverage. Sentinel absence supports confidentiality
   but does not prove it. Verify a copied backup can authenticate/decrypt fictional
   records using the separately recovered ring under the authorized actor.
6. Update terminal instructions and public XML comments. Run restore, formatting,
   Release build, NUnit, pending-model checks and current Windows/Linux PR CI.

#66 retains coordinated recovery publication, rotation, retention and old-backup
rehearsals before real Sensitive data is permitted. Keep #25, #72 and #26 open
until their complete acceptance criteria have been reconciled; completing this
task alone does not claim whole-system security or production readiness.

## Alternatives and tradeoffs

- Granting all readers Sensitive access is simpler but defeats the explicitly
  deferred policy boundary. Owner-only access is more restrictive; the proposed
  Owner/Administrator policy supports delegated management without giving Editors
  private-value access.
- One mixed table offers one database name index but requires rebuilding the
  existing table and a carefully constrained dual representation. Separate tables
  preserve ordinary rows and avoid plaintext-capable Sensitive storage, at the
  cost of a cross-table name check under the serialized writer boundary.
- Adding attribute IDs to the Domain model and every existing Ordinary API would
  broaden this slice. Stable storage identity is sufficient for authenticated
  binding while retaining the existing Asset-local Domain behavior.
- Automatic EF converters risk decrypting values during unauthorized materialization.
  Explicit mapping and deliberate reads keep the access boundary inspectable.
- Existing EF Core/SQLite, ASP.NET Core security, Ardalis.GuardClauses and the
  merged framework-based encryption/custody code are sufficient. No custom
  cryptographic primitive, generic repository framework or new NuGet package.
- Unattended unlocking requires a separately reviewed secret-delivery mechanism.
  The explicit interactive host keeps the accepted no-persisted-recovery-secret
  rule; restart frequency and ring capacity remain operational constraints.

## Confirmation

Pending owner review of the Sensitive role policy, routes, separate-table/stable-ID
design and interactive host lifetime. No application code or migration is included
in this proposal; acceptance will authorize the implementation sequence above.
