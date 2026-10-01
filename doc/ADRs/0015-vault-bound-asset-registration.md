# ADR-0015: Vault-bound Asset registration in Playground

Status: Accepted
Created: 2026-10-01
Accepted: 2026-10-01
Issue: [HV-19 / #23](https://github.com/mani8785/HomeVault/issues/23)

## Context

HV-19 asks for an executable Vault creation and Asset registration scenario using
Application and in-memory adapters. Vault creation is integrated under
[ADR-0014](0014-in-memory-vault-infrastructure.md). Asset.Create currently creates
an unowned, transient domain object. Presenting these unrelated calls as one
stored journey would hide the missing integration.

[ADR-0004](0004-domain-language-and-boundaries.md) requires persisted Assets to
belong to one Vault and writes to use current authorization/lifecycle state.
The [repository follow-up checklist](0013-vault-repository-contract.md) explicitly
defers Asset storage until a concrete scenario is reviewed. This is that scenario.

## Proposed decisions

1. Add an immutable VaultId to Assets created through a new Vault-bound factory
   overload. Require a non-empty Vault Guid. Preserve the existing unbound factory
   for domain demonstrations and compatibility; mark its objects as unbound with
   a nullable VaultId. Storage rejects unbound Assets. An Asset cannot change
   Vault ownership through either API. Use the existing Guid/name validation
   and guard preference without adding new packages.
2. Add RegisterAssetUseCase with injected ICurrentActor and an Application-owned
   IAssetRegistrationStore. Its async request contains Asset id, Vault id, and
   name, but no actor/owner nomination. Return a project-owned result with an
   immutable id/VaultId/name view only after insertion succeeds. Diagnostics omit
   metadata. Preserve names exactly as existing Asset creation does.
3. Define one purpose-specific atomic registration operation accepting a newly
   created Vault-bound Asset, the trusted actor Guid, and cancellation. Check
   Vault existence, membership, allowed role (Owner/Administrator/Editor), and
   Active status together with insertion. Missing and inaccessible Vaults return
   the same VaultUnavailable outcome. Authorized Viewers receive Forbidden;
   authorized writers to an archived Vault receive VaultArchived. Check access
   before reporting an Asset identity conflict; never expose the existing Asset.
4. Asset identities are unique across the shared store. Never overwrite or move
   an existing Asset, including one in another Vault. Store an independent
   creation snapshot with no attributes or Evidence yet; reject prepopulated
   instances as programming errors rather than silently dropping their data.
5. Use an explicitly shared InMemoryHomeVaultStore backing the Vault repository
   and new Asset registration adapter. A common lock covers Vault state checks
   and Asset insertion. Share one store in Playground; separate stores remain
   isolated. Preserve the convenient parameterless Vault repository constructor
   by letting it create its own store. All future membership/archive mutations
   must use this same atomic boundary or an equivalent reviewed mechanism.
6. No public Vault read/update/list APIs are added merely for this scenario.
   Snapshot inspection and controlled state seeding remain internal to the
   Infrastructure test assembly so tests can cover roles and archived state.
   These test seams must not become public authorization bypasses. Production
   currently stores new Vaults with one Owner and has no membership/archive
   storage mutation path; do not claim such paths are implemented.
7. Application validation order: null request is a programming error; then
   pre-cancellation, current actor, Asset id, Vault id, name, and finally atomic
   store checks. Read identity once; do not call storage for invalid input.
   Map expected outcomes to safe application errors, propagate cancellation and
   unexpected failures without retries, and do not equate success with durability.
8. Replace the disconnected Playground walkthrough with a clearly labeled main
   journey: create a Vault, register a fictional bicycle in it, show duplicate
   rejection, invalid name, unavailable Vault, and missing actor. Retain existing
   domain demonstrations separately if useful. Assert expected outcomes so an
   unexpected result exits nonzero instead of printing a misleading success.

## Alternatives and tradeoffs

A composition-only change could show the already approved standalone operations,
but would not demonstrate an Asset registered in the created Vault. Separate
read-then-insert checks would permit stale authorization once mutations exist.
A generic repository or public GetAll/Update API would exceed this concrete need.
Keeping an unbound domain factory avoids breaking existing domain-only callers,
but requires an explicit storage boundary that rejects unbound objects.

The shared store coordinates the two adapters without a service locator or DI
container. This is a process-memory design, not a database/ORM choice. Real
authentication, durable storage, other Asset mutations, and notifications remain
outside this step. Future persistence must satisfy the same atomic guarantees.

## Implementation tasks after acceptance

1. Add Vault-bound Asset creation and test validation, preserved metadata, and
   compatibility with existing unbound domain operations.
2. Add the registration application contract/result/use case and test ownership
   binding, validation precedence, cancellation, awaited insertion, and failures.
3. Implement the shared store and adapter; test missing/inaccessible Vaults,
   roles, archive rejection, duplicate identities across Vaults, concurrent
   insertion, independent snapshots, and isolated store instances.
4. Compose and assert the Playground journey; document terminal commands and
   limitations. Update architecture and affected prior implementation records.
5. Run restore, formatting, Release build, all NUnit tests, Playground, and PR CI.
   Leave the PR open for explicit scenario review before persistence selection.

## Confirmation

The owner explicitly confirmed all proposed items on 2026-10-01: Vault-bound Asset
creation, atomic registration, shared in-memory storage, and the verified journey.
The accepted Vault insertion contract and role matrix remain unchanged.
