# ADR-0032: Authorized Vault and Asset renaming

Status: Accepted
Created: 2026-10-12
Accepted: 2026-10-12 (explicit owner confirmation in this chat)
Related: [HV-25.1 #116](https://github.com/mani8785/HomeVault/issues/116)

## Context

Vault and Asset creation and browsing are implemented, but names cannot be
changed. Renaming must preserve identities, ownership, dependent records and
current archive/access rules. Two users editing a name must not silently
overwrite each other's last-read state.

## Proposal

- Vault renaming permits current Owner and Administrator roles. Asset renaming
  permits current Owner, Administrator and Editor roles. Viewer cannot rename.
- Both operations require an Active Vault, including unchanged-name requests.
- Reuse existing nonblank name validation. Preserve whitespace, case and Unicode
  exactly; introduce no length limit, normalization or uniqueness requirement.
- Each request supplies `name` and `expectedName`, the exact last-read name.
  Compare ordinally after current access and archive checks in the same SQLite
  non-deferred write transaction as the update. A mismatch returns a conflict;
  an unchanged name with matching expectation succeeds without changing state.
- Use purpose-specific Application contracts implemented by Infrastructure;
  validated Domain behavior replaces only the name. No schema migration or
  generic update repository is needed. Never load Sensitive values to rename.
- Expose authenticated, antiforgery-protected `PUT /api/v1/vaults/{vaultId}/name`
  and `PUT /api/v1/assets/{assetId}/name` with only the two name fields. Return
  204 on success; 400 for malformed identity/body or blank names; 401 for no
  actor; 404 identically for missing/inaccessible records; 403 for insufficient
  current role; 409 `vault_archived` or `name_conflict` as appropriate. Errors
  must not echo either submitted or stored names.
- Resolve an Asset's actual Vault in storage. Serialize renaming with archive
  and membership mutations; preserve Guid identity, Vault type, membership,
  attributes, Evidence, Relationships, Reminders and encrypted record binding.
- Angular offers a labelled rename form only for eligible current roles, sends
  the last-read name, and refreshes metadata after success. A conflict preserves
  the draft and asks the user to reload before deliberately retrying. Server
  authorization remains authoritative; no automatic retry overwrites a conflict.

## Alternatives and consequences

Last-write-wins is smaller but can silently discard a user's change. A numeric
revision or ETag supports broader editing but adds schema/version infrastructure
before that need is approved. Comparing the expected name needs no migration,
but cannot detect an intervening rename that returns to the same name (ABA).
This proposal accepts that limitation for this narrow operation; broader
metadata editing remains a separate decision in #117.

## Validation and delivery

Add behavioral coverage for role boundaries, archive and permission races,
stale edits, invalid names, unchanged failures, exact names, stable identity and
related-data preservation across reload. Add HTTP antiforgery/isolation and UI
conflict handling coverage. The owner runs tests and reviews CI and the PR.
No deletion, restoration, Vault transfer or metadata-editing scope is included.

## Confirmation

The owner explicitly approved the role, validation, active-only, expected-name,
antiforgery and identity-preservation contract on 2026-10-12.
