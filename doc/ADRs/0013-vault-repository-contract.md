# ADR-0013: Atomic Vault creation repository contract

Status: Accepted
Created: 2026-10-01
Accepted: 2026-10-01
Issue: [HV-17 / #21](https://github.com/mani8785/HomeVault/issues/21)

## Context

[ADR-0012](0012-first-application-use-case.md) implements transient Vault
creation for the current actor. HV-17 asks for only the persistence operations
needed by this scenario. Application owns technical contracts under
[ADR-0003](0003-selective-dependency-injection.md); Infrastructure implements them.
No repository contract or adapter currently exists.

## Proposed decision

1. Add Application-owned `IVaultRepository` with one operation:
   `Task<VaultAddOutcome> AddAsync(Vault vault, CancellationToken cancellationToken)`.
   The outcome is `Added` or `IdentityConflict`. No generic repository, query,
   update, delete, or separate existence-check operation is introduced.
2. Add is insert-only and atomic across the entire repository identity scope.
   A missing identity is inserted; an existing identity is never overwritten,
   even for the same owner or identical data. Concurrent attempts with the same
   Guid permit exactly one successful insertion. A conflict reveals no existing
   Vault metadata or owner. This is not an idempotent retry contract.
3. The caller supplies a domain-created Active Vault with exactly one Owner.
   Application binds that owner to its trusted current actor before calling the
   repository. The repository does not authenticate callers or select owners.
   The Vault and initial membership are stored atomically. Null or non-creation
   state is a programming error, documented as an argument exception.
4. Successful insertion stores an independent snapshot: later mutations of the
   supplied domain instance cannot change stored state. The caller must not
   mutate the instance while AddAsync is running. No ORM or serialization types
   cross the boundary. Concrete snapshot implementation belongs to HV-18.
5. `Added` means the adapter accepted the write, not necessarily durable disk
   storage. Expected duplicate identities use the explicit outcome. Cancellation
   and unexpected storage failures propagate; callers must not infer that an
   interrupted or failed response proves no commit occurred. Adapters must honor
   already-requested cancellation before changing state. No retries or sensitive
   exception-message logging are introduced by this contract.
6. Missing-record reads are not applicable to creation: there is no lookup API
   yet. Existing-record authorization, archive races, version checks, and
   cross-aggregate transactions remain required by ADR-0004 when mutation/read
   scenarios are added. This insert-only contract cannot implement those rules.
7. HV-17 delivers the documented contract and outcome only. Keep the accepted
   transient CreateVaultUseCase behavior unchanged until HV-18 adds the adapter
   and a reviewed integration step binds successful application creation to
   storage. No production adapter, fake production persistence, database, or
   new dependency is introduced merely to make this interface executable.

## Alternatives and tradeoffs

A generic CRUD repository adds unused operations with undefined access rules.
A lookup followed by insert cannot guarantee uniqueness under concurrency.
Upsert could overwrite another owner's Vault. Synchronous methods fit memory
storage but would constrain future I/O adapters; Task and CancellationToken use
only base libraries. Deferring lookup keeps HV-17 tied to the sole existing
application use case, at the cost of needing a reviewed read scenario later.

## Follow-up repository checklist

The contract is independent of the selected storage technology: Application
uses domain types and base-library asynchronous types, while Infrastructure
chooses how to satisfy the contract. In-memory, file, or database adapters must
meet the same observable guarantees. A future storage choice may expose a need
to review the contract; independence does not make consistency requirements free.

These are planned capabilities, not unused interface methods or approved scope:

- [ ] Get by identity for a concrete inspection or mutation use case. Define
  missing/inaccessible behavior, actor access, and detached snapshot semantics.
- [ ] List accessible Vaults or Assets with explicit access scope, stable ordering,
  and pagination. Prefer bounded queries over an unrestricted GetAll operation.
- [ ] Save approved mutations with an expected version or equivalent atomic
  concurrency guarantee. Define conflict/retry behavior and protect membership
  and archive decisions against stale writes; avoid unconditional overwrite.
- [ ] Add Asset storage when the Vault-bound registration scenario is refined.
- [ ] Define archive persistence and read visibility. Hard deletion and cascading
  remain deferred; do not introduce Delete merely to complete a CRUD interface.
- [ ] Run shared behavioral contract tests against each implemented adapter,
  covering applicable duplicates, missing records, snapshots, and concurrency.

Existing backlog coverage is partial:
[HV-18 / #22](https://github.com/mani8785/HomeVault/issues/22) implements agreed
in-memory contracts; [HV-19 / #23](https://github.com/mani8785/HomeVault/issues/23)
composes Vault creation and Asset registration;
[HV-20 / #24](https://github.com/mani8785/HomeVault/issues/24) selects durable
storage, transactions, and save/reload tests;
[HV-22 / #26](https://github.com/mani8785/HomeVault/issues/26) enforces access.
None explicitly owns a complete get/list/update repository slice. Before closing
planning for those capabilities, refine dedicated tasks around their application
scenarios; do not assume the infrastructure or storage-selection issue covers
them automatically.

## Implementation and verification after acceptance

1. Add the narrow Application interface and explicit outcome with meaningful XML
   documentation covering identity, ownership, atomicity, snapshots, and failures.
2. Add the implementation record and link this ADR. Review the interface against
   the existing creation path and accepted dependency constraints.
3. Run restore, formatting verification, Release build, existing NUnit tests,
   Playground, and diff checks. Do not claim interface declarations demonstrate
   concurrency or storage isolation: HV-18 must test a real adapter's duplicate,
   concurrent insertion, cancellation, and snapshot behavior.
4. Open the HV-17 PR and verify CI. Leave it open for owner review.

## Confirmation

The owner explicitly confirmed the persistence boundary and contract-only scope
on 2026-10-01 after reviewing storage independence and the follow-up checklist.
Get/list/update capabilities remain planned rather than part of this interface.
