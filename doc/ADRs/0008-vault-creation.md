# ADR-0008: Initial Vault creation contract

Status: Accepted
Created: 2026-09-28
Accepted: 2026-09-28
Issue: [HV-10 / #14](https://github.com/mani8785/HomeVault/issues/14)

## Context and existing decisions

[ADR-0004](0004-domain-language-and-boundaries.md) already accepts Personal,
Household, and Organization Vaults, Active creation, and an initial Owner.
Personal does not imply a one-member limit. Vault owns memberships but does not
load all Assets. These decisions remain accepted; this proposal refines the
creation inputs and naming rules that were explicitly left open.

## Proposed creation contract

1. Expose Vault.Create(Guid id, string? name, VaultType type, Guid initialOwnerId).
   Both identifiers must be non-empty. The caller supplies them; no identity
   generation, uniqueness lookup, or actor-existence check occurs in Domain.
   The owner identifier refers to an actor, not an Asset representing a person.
2. Require a nonblank name and preserve it exactly, consistent with Asset names.
   Do not introduce trimming, normalization, uniqueness, or arbitrary length
   limits. Names may be private and must not appear in diagnostics.
3. Accept only Personal, Household, or Organization. Successful creation returns
   an Active Vault with exactly one immutable membership: initialOwnerId as Owner.
   Expose memberships through a read-only snapshot. No partially valid Vault
   escapes when any input is invalid.
4. Return a concrete VaultCreationResult with either the Vault or a safe error.
   Validate in parameter order: id, name, type, initialOwnerId. Failures are
   EmptyIdentity, BlankName, InvalidType, and EmptyOwnerIdentity; success has None.
   Use approved Ardalis.GuardClauses for standard guards, mapping only expected
   argument failures. No supplied input or exception message appears in results.
5. Define the Asset relationship as a single VaultId reference, never a loaded
   Vault object or a Vault-owned Asset collection. This slice documents that
   integration contract; it does not change the current standalone Asset factory.
   Actual registration integration must require a valid VaultId and verify Vault
   existence, lifecycle, and actor access in Application when that use case is
   implemented. Domain creation alone does not establish authorized storage.

## Alternatives and tradeoffs

A nameless Vault requires fewer inputs but gives users no descriptive label.
Reusing AssetName for Vault names would couple unrelated concepts; use a small
Vault-specific validation boundary instead. Automatic identity generation or
owner resolution introduces collaborators unnecessary for domain creation.
Adding an optional VaultId to Asset now would create an ambiguous intermediate
ownership state; retain the documented standalone scope until registration is
implemented with its required checks.

## Implementation tasks after acceptance

1. Implement the creation factory, concrete result/errors, Vault types/status,
   and immutable initial membership. Preserve the accepted role vocabulary.
2. Add NUnit tests for all three types, exact naming, every invalid input,
   error precedence, initial ownership, collection protection, and safe output.
3. Document the reference boundary and demonstrate a fictional Vault creation
   and failure in Playground, without displaying private names or actor IDs.
4. Run restore, format verification, Release build, NUnit tests, Playground,
   and PR CI. Open the implementation PR for owner review.

Membership changes and last-owner enforcement follow HV-11; archival operations
follow HV-12. Authentication, persistence, events, and generic DDD base classes
are outside this slice. No new package or script is needed. This issue has no
linked subtasks; the checklist above is sufficient for the small implementation.

## Confirmation

The owner explicitly confirmed this creation contract on 2026-09-28. The existing decisions
in ADR-0004 remain accepted and are not being submitted for approval again.
