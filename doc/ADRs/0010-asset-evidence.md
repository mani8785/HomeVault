# ADR-0010: Initial Asset evidence contract

Status: Accepted
Created: 2026-09-29
Accepted: 2026-09-29
Issue: [HV-14 / #18](https://github.com/mani8785/HomeVault/issues/18)

## Context

[ADR-0004](0004-domain-language-and-boundaries.md) makes Evidence an Asset-owned
component containing supporting reference metadata. It does not store binary
data, fetch URLs, or delete external resources. Reference formats and mutation
rules were deferred to HV-14; the issue has no discussion or linked subtasks.

## Proposed rules

1. Start with Url and Note kinds. A URL can point to an external receipt or
   document; a Note holds supporting text. File-path references, document-store
   IDs, attachments, uploads, and document resolution remain future capabilities.
2. Each entry has a caller-supplied non-empty Guid, a nonblank label preserved
   exactly, a supported kind, and nonblank content preserved exactly. The Guid
   addresses the entry within its owning Asset; Evidence is not a separate root.
   No arbitrary text-length limit or normalization is introduced.
3. Url content must parse as an absolute HTTP or HTTPS URI with a non-empty host,
   no user-info credentials, and no raw whitespace or control characters. Query
   strings and fragments are allowed; they may be sensitive. Preserve the input
   instead of Uri's normalized representation. Do not test reachability or claim
   that a valid URL is safe to fetch. Notes have only the nonblank text constraint.
4. AddEvidence validates identity, label, kind, content, then URL format, then
   duplicate identity. Return safe errors with no supplied data. Reject duplicate
   IDs within one Asset; allow multiple independently identified entries with the
   same label/content. Failed additions leave all existing entries unchanged.
5. RemoveEvidence validates the identity and rejects a missing entry with
   NotFound. Success removes the entry only from that Asset; it never deletes
   a referenced resource. No edit operation is introduced in this slice.
6. Expose immutable entries in read-only snapshots. Labels remain visible
   metadata and must not contain secrets. Keep content private and provide
   ReadContent() for deliberate access; omit a public raw-content property.
   ToString and failures omit labels/content/identities. Default System.Text.Json
   serialization must not reveal content. This reduces accidental disclosure;
   it is not authorization, encryption, or protection against custom serializers.
   Earlier snapshots and strings already read cannot be revoked.

## Scope and access boundary

Implement the Asset-owned domain behavior, tests, and a fictional Playground
scenario without fetching URLs or printing evidence content. As accepted for
the existing standalone Asset model, no VaultId or authenticated application
boundary exists yet. Thus the PR cannot enforce actual Vault access or archive
restrictions on Evidence at runtime. Those operations must pass through the
future application layer's ownership, actor-access, and lifecycle checks.

Keep HV-14 open after this domain PR; the access acceptance criterion remains
until integration tests prove isolation and archived-write rejection. This
proposal does not redefine safe formatting as access control or authorize
unplanned storage/authentication work.

## Alternatives and tradeoffs

Supporting every reference kind now requires unresolved file/document locator
semantics. URLs and notes provide concrete usable metadata first. Matching by
label or content makes removal ambiguous; caller-supplied entry IDs give stable
targets. Content-method access makes reads explicit in reviews and keeps default
property serialization from emitting private notes or token-bearing URLs, but
future persistence must deliberately read content rather than serialize the
public inspection view as a lossless record.

## Tasks after acceptance

1. Implement validated immutable Evidence entries and safe operation errors.
2. Add Asset add/remove operations and snapshot inspection.
3. Test supported formats, invalid URLs/inputs, validation order, duplicate IDs,
   removal, failure atomicity, snapshot/Asset isolation, and safe serialization.
4. Update documentation and Playground; run restore, format verification,
   Release build, NUnit tests, smoke test, and PR CI.

## Confirmation

The owner explicitly confirmed these rules and the limited domain scope on
2026-09-29, including later file/document resolution through Application and Infrastructure.
Existing Asset ownership and external-resource boundaries remain accepted.
