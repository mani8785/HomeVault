# ADR-0014: In-memory Vault storage and application integration

Status: Accepted
Created: 2026-10-01
Accepted: 2026-10-01
Issue: [HV-18 / #22](https://github.com/mani8785/HomeVault/issues/22)

## Context

[ADR-0013](0013-vault-repository-contract.md) defines the accepted insert-only
repository contract. The next step implements that contract and explicitly
composes it. [ADR-0012](0012-first-application-use-case.md) currently guarantees
only transient creation. Existing tests may reference Domain and Application
only, so adapter testing needs an explicit extension of the test boundary.

## Proposed decisions

1. Implement InMemoryVaultRepository in Infrastructure using instance-owned
   memory and an atomic insertion operation. Each instance is an independent
   store; sharing the same instance shares its contents. No static global state,
   database, disk access, DI container, or new package is needed.
2. Store an immutable private snapshot of id, name, type, Active status, and the
   sole initial Owner. Validate the accepted creation-state precondition. Do not
   retain the caller's mutable Vault. Honor pre-cancellation before mutation and
   check cancellation at the insertion boundary. Concurrent duplicate insertions
   produce exactly one Added result; all others return IdentityConflict.
3. Add HomeVault.Infrastructure.Tests as a separate NUnit project using existing
   package versions. It may reference Infrastructure, Application, and Domain.
   Existing HomeVault.Tests dependencies remain unchanged. Extend architecture
   validation and solution membership to cover the new project. This supplements
   the initial test boundary in ADR-0002 without changing production dependencies.
   Permit internal test access to an immutable stored snapshot to verify actual
   isolation and preservation; this does not add a public repository read API.
4. Inject IVaultRepository alongside ICurrentActor into CreateVaultUseCase.
   Replace Execute with ExecuteAsync(request, cancellationToken). After the
   existing actor and domain validation, await insertion exactly once. Return
   the immutable created view only on Added; map IdentityConflict to a new safe
   application error. Unexpected failures and cancellation propagate without
   automatic retries or exposing exception messages. A pre-cancelled call stops
   before reading actor state. Retain null-request programming-error handling.
5. This supersedes only ADR-0012's transient-success semantics and synchronous
   entry point. Success now means acceptance by the injected adapter, which is
   still not durable persistence. Authentication, get/list/update operations,
   existing-Vault authorization, and Asset integration remain separate work.
6. Manually construct and share one repository instance in Playground. Demonstrate
   successful creation, duplicate identity rejection, and unchanged validation
   failures, printing safe outcomes. Missing-record lookup is inapplicable to the
   approved insert-only contract; no speculative GetById is introduced.

## Alternatives and consequences

Leaving the use case transient would implement the adapter but would not exercise
storage through Application. Adding Infrastructure to the existing test project
would weaken its Domain/Application-only boundary. A separate project keeps
adapter dependencies explicit at the cost of another small project. Internal
snapshot inspection is limited to tests and avoids a public read operation with
unapproved access semantics. It must not become a production bypass for reads.

The asynchronous entry point is a breaking change for the current Playground and
tests; update all existing callers together. Stored data disappears with the
repository instance or process. Durable storage remains HV-20.

## Tasks and verification after acceptance

1. Implement the adapter and test insert, conflicting identity across owners,
   invalid creation state, cancelled insertion, concurrent duplicates, per-instance
   lifetime, preserved existing data, and independence from caller mutations.
2. Integrate async application creation and test success, conflict, validation
   without storage calls, cancellation, and propagated storage failures.
3. Update Playground, architecture documentation/tests, solution, and ADR history.
4. Run restore, format verification, Release build, all NUnit tests with executed
   counts, Playground, diff checks, and PR CI. Leave the PR open for review.

## Confirmation

The owner explicitly confirmed all proposed items on 2026-10-01, including the
new test-project boundary and application storage integration. ADR-0013 remains
accepted; this decision supplements ADR-0002 and partially supersedes ADR-0012.
