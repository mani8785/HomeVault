# ADR-0022: Durable authorization for remaining operations

Status: Accepted
Created: 2026-10-02
Accepted: 2026-10-02
Issue: [HV-22.4 / #72](https://github.com/mani8785/HomeVault/issues/72)

## Context

[ADR-0020](0020-local-accounts-vault-authorization.md) already accepts the role
matrix, current membership checks, strict archival, and atomic writes. PR #75
delivers three authenticated HTTP operations. Domain membership, attributes,
Evidence, Relationship and Reminder behavior exists, but its durable application
integration does not. Issue #72 requires bounded tasks and reviewed contracts
before implementation. The existing Memberships table has no Identity foreign
key; fictional Playground actors remain supported by earlier storage contracts.

This proposal supplements ADR-0020; it does not reopen its accepted role matrix,
choose another database, or authorize the parked UI/encryption work.

## Proposed decisions

### 1. Begin with archive, then membership

Implement ArchiveVault first, using the existing Vaults/Memberships schema.
POST /api/v1/vaults/{vaultId}/archive has no business request body and returns
204 for an Owner, including repeated requests on an already Archived Vault.
Other current members receive 403; missing/nonmember requests share 404.
Authentication and antiforgery remain mandatory. Archival is irreversible through
this API and must serialize with Asset registration and future mutations.

Next expose POST /api/v1/vaults/{vaultId}/members with targetActorId and role,
PUT /api/v1/vaults/{vaultId}/members/{targetActorId}/role with role, and
DELETE /api/v1/vaults/{vaultId}/members/{targetActorId}. Success returns 204.
Role strings are owner, administrator, editor, viewer. The target is an operation
argument, never the requesting actor; the latter always comes from ICurrentActor.

An Owner may manage all roles while retaining at least one Owner. An Administrator
may add Editor/Viewer and change/remove only existing Editor/Viewer memberships;
both the old and new role must be permitted. Editor/Viewer cannot manage members.
An unchanged role succeeds only after authorization and the Active-state check.
Removing a missing member returns 404, and duplicate addition/last-Owner failure
returns 409. Archived membership writes return 409 after access/role checks.

For additions, require an existing enabled HomeVault account, verified inside the
same transaction after requester authorization. Missing/disabled targets share
one safe unavailable result. Obtain the target Guid out of band from the enrolled
user (their existing /auth/session response); introduce no public account search,
email lookup, account creation, or invitation sending through Vault membership.
Keep the existing storage schema and fictional Playground contracts unchanged.
Changing/removing an existing membership must remain possible when its target
account is disabled; disabling an account does not silently delete membership or
change the accepted last-Owner invariant. Lockout is not account deletion.

### 2. Rehydrate domain state and keep mutation boundaries atomic

Application owns narrow operation-specific contracts and safe outcomes, not a
generic CRUD repository. Infrastructure acquires the existing non-deferred SQLite
write transaction before reading current membership/lifecycle, restores the
required domain state, invokes domain behavior and persists the resulting changes
before commit. No caller reads permission and later performs an unguarded write.

Introduce explicit validated restoration factories only as each slice needs them.
Restoration preserves identity, state and memberships without replaying creation
or emitting creation events. It rejects invalid stored enums, duplicate members,
empty identities, or a Vault without an Owner. Restore a Vault with its members,
not all Assets. Keep persistence row types, EF attributes and DbContext out of
Domain; never use reflection or public setters to bypass invariants. Invalid
stored state produces a safe unexpected failure, never a partially restored object.

Domain retains membership/lifecycle invariants. The atomic Infrastructure adapter
enforces current requester access and identity-store facts under the Application
contract, following the existing Asset registration boundary. Add contention tests
with independent connections, including two Owners attempting to remove/demote
each other. Do not add automatic business retries or claim a failed HTTP response
proves no commit occurred.

### 3. Add each remaining capability as a separate reviewed slice

Ordinary attributes, URL/Note Evidence, directed Relationships and Reminders each
receive their own Application operations, SQLite mappings/migration, HTTP DTOs,
OpenAPI updates and tests. Review exact schema, routes and failure precedence in
the relevant slice before code; this ADR does not pre-approve unspecified columns.

Preserve [attribute sensitivity](0007-sensitive-attributes.md),
[Relationship semantics](0009-asset-relationships.md),
[Evidence boundaries](0010-asset-evidence.md), and
[Reminder lifecycle](0011-asset-reminders.md). Ordinary attribute operations reject
Sensitive requests and must never read, overwrite or downgrade Sensitive values.
Evidence supports URL/Note metadata only: no uploads, fetching or document resolver.
Relationship creation checks both endpoints against their actual stored Vault and
enforces active-tuple uniqueness atomically; removal retains the root lifecycle.
Reminder operations preserve UTC instants and terminal-state rules, with no scheduler.

All reads use current membership-scoped queries, including reads from Archived
Vaults. Writers are Owner/Administrator/Editor on Active Vaults. Check access
before existence/conflict details that could disclose another Vault's data.
Use safe purpose-specific DTOs rather than serializing domain or storage entities.
Each migration needs upgrade-preservation and backup/restore compatibility tests.

## Alternatives and consequences

- Raw row mutations alone are smaller but can duplicate or bypass existing Domain
  invariants; explicit restoration keeps those rules executable and testable.
- A generic load/edit/save repository separates permission checks from commit;
  narrow atomic contracts retain the accepted SQLite concurrency guarantees.
- An Identity foreign key on all existing memberships would break fictional actor
  databases and needs migration/import policy. Check real targets at the account
  application boundary instead; this does not claim database-wide account integrity.
- Email-based member lookup is more convenient but introduces account discovery
  and mutable-identifier policy. Guid-based addition is a deliberately limited first
  workflow; a future directory/invitation UX needs a separate decision.
- Existing EF Core transactions, Identity stores, ASP.NET authentication/antiforgery
  and project-owned results suffice. No new package, dispatcher, script or framework
  dependency in Domain/Application is proposed.

## Delivery and acceptance

See the [bounded task plan](../hv-22-remaining-operations.md). Each implementation
needs application tests, real-file SQLite contention/rollback tests and HTTP tests
with two actual accounts and cookies. Verify all roles, archived writes, current
membership changes, missing/inaccessible equivalence, forged requester input,
antiforgery, safe failures and reload after restart. Run the repository's restore,
format, Release build, NUnit and current-revision Windows/Linux CI checks.

The first review should settle decisions 1 and 2 so ArchiveVault can start without
waiting for unrelated schemas. Decision 3 defines the bounded sequence, not approval
of all future API/schema details. Keep #72 and parent #26 open until their required
coverage is delivered; Sensitive policy/encryption remains an explicit dependency.

## Confirmation

The owner explicitly confirmed the operation contracts, real-account membership
target policy and validated restoration approach on 2026-10-02. ArchiveVault (#76)
is authorized as the first implementation slice. Later schemas/contracts still
require their stated review. Existing accepted decisions remain in force.
