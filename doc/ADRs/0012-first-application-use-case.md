# ADR-0012: Create a Vault for the current actor

Status: Accepted; transient-success semantics and synchronous entry point superseded by [ADR-0014](0014-in-memory-vault-infrastructure.md) on 2026-10-01
Created: 2026-09-29
Accepted: 2026-09-29
Issue: [HV-16 / #20](https://github.com/mani8785/HomeVault/issues/20)

## Context

Application currently has no implementation. HV-16 calls for the first agreed
scenario, then small reviewed extensions. Domain creation and later mutations
already exist; repository contracts, in-memory infrastructure, and authentication
are separate roadmap items. No issue discussion or subtasks select a scenario.

## Proposed scenario and boundary

1. Implement CreateVaultUseCase in Application. Its request contains a supplied
   non-empty Vault Guid, name, and VaultType; it does not accept an owner identity.
   The current actor becomes the initial Owner through the existing Vault factory.
2. Define a narrow Application-owned ICurrentActor contract exposing a nullable
   actor Guid. Inject it into the use case constructor. A null or empty identity
   returns Unauthenticated before request-field validation; do not call Domain.
   Read the actor once per execution and do not cache it across executions.
3. The contract is a trusted identity boundary, not an authentication provider.
   Future Infrastructure must populate it from verified authentication context;
   clients must not populate it from an arbitrary request field. Playground and
   tests use explicit fictional implementations. No real sign-in or authorization
   security is claimed by this first scenario.
4. On a valid actor, call Vault.Create using its identity as initialOwnerId.
   Map domain failures to concrete application error codes: EmptyIdentity,
   BlankName, InvalidType. Return a project-owned result with either an immutable
   created-Vault view or the safe error, never a mutable domain entity. The view
   includes identity, original name, type, Active status, and initial owner ID.
   Its deliberate reads may reveal private metadata; default ToString must not.
5. Keep caller-supplied identities and domain validation order unchanged. The
   use case creates a transient result; it does not save, look up, or enforce
   uniqueness. A result means successful domain creation, not persistence.
   Repository integration must extend this operation before a real client can
   expect the created Vault to survive the current process.
6. Demonstrate the entire first application call path in Playground: manually
   construct the fictional current-actor adapter and use case, invoke success
   and validation failures, and print safe outcomes only. Test success, missing
   identity, error precedence, initial ownership, and identity changes between
   calls. Existing domain tests remain authoritative for local invariants.

## Alternatives and tradeoffs

Passing ownerId in the request would allow clients to nominate an arbitrary
owner rather than binding creation to the caller. Implementing an authentication
provider now would preempt HV-22. Returning a mutable Vault would expose domain
mutations directly to application consumers; an immutable result gives this
entry point a bounded contract. Persistence is required for durable end-to-end
behavior but its contracts are intentionally refined in HV-17.

## Delivery and subsequent work

This step establishes and tests the client-to-Application-to-Domain scenario.
It is explicitly transient, with no storage or real authentication. No generic
CRUD layer, container, service locator, database, or new package is needed.

Next reviewed slices add concrete repository contracts (HV-17), in-memory
adapters (HV-18), and durable scenario composition (HV-19). Asset registration,
cross-Vault access, archive write enforcement, and relationship/evidence/reminder
integration remain subsequent scoped use cases; this PR must not claim them
complete or close their outstanding integration work.

## Implementation tasks after acceptance

1. Add the identity contract, explicit constructor injection, request/result,
   immutable view, and CreateVaultUseCase with meaningful XML documentation.
2. Test the complete application call path and failure behavior.
3. Wire the fictional scenario in Playground and document the transient result.
4. Run restore, format verification, Release build, NUnit tests, Playground, and
   PR CI; leave the PR open for review.

## Confirmation

The owner explicitly confirmed this first scenario and identity boundary on 2026-09-29.
Existing project dependencies, Vault rules, and selective DI remain accepted.
