# ADR-0009: Initial Asset relationship contract

Status: Accepted
Created: 2026-09-28
Accepted: 2026-09-28
Issue: [HV-13 / #17](https://github.com/mani8785/HomeVault/issues/17)

## Context

[ADR-0004](0004-domain-language-and-boundaries.md) accepts an independently
identified, directed Relationship root containing one VaultId and two AssetIds.
Application checks endpoint existence, ownership, and access. It explicitly
defers relationship kinds, self-links, duplicates, and lifecycle details to
HV-13. No issue comments or linked subtasks settle those details.

## Proposed rules

1. Begin with one concrete kind: Covers, directed from an insurance-policy Asset
   to the covered Asset. Reject undefined kind values. The current Asset model
   has no categories, so these endpoint meanings are caller responsibilities,
   not inferred or falsely validated by Domain. Additional kinds require their
   own agreed semantics; no automatic reverse Relationship is created.
2. Require non-empty relationship, Vault, source Asset, and target Asset Guids.
   Reject equal source/target identities. Preserve all supplied identities and
   keep references as identities; never load or embed an Asset graph.
3. Define duplicates as the same VaultId, source AssetId, target AssetId, and kind
   among active relationships, regardless of relationship identity. Reject them
   at the application/persistence boundary, with atomic uniqueness enforcement
   when storage exists. Reverse direction is a distinct tuple.
4. Remove means a one-way transition from Active to Removed on the Relationship
   root. Retain identities and kind for inspection; never delete either endpoint.
   Repeated removal succeeds unchanged. No reactivation or endpoint edits are
   introduced. A removed relationship no longer participates in active duplicate
   checks, so a later new relationship may use the same tuple.
5. Retain the accepted same-Vault rule: both endpoints must exist in the
   Relationship's Vault, and inaccessible/missing endpoints must not leak data.
   Application must resolve the actual endpoint VaultIds and check actor access
   and Active Vault state before creation/removal. A caller-supplied VaultId
   alone is not proof of ownership. Archive races require the accepted
   concurrency enforcement at the persistence boundary.
6. Use project-owned creation results and safe errors. Validate relationship id,
   Vault id, source id, target id, kind, then self-reference, in that order.
   Use Ardalis.GuardClauses for standard guards. Do not format identifiers or
   supplied data in diagnostics; no event consumer or dispatch mechanism exists.

## Scope and completion boundary

Implement the domain root, validation, removal, tests, documentation, and a
fictional Playground example in this step. This preserves the accepted roadmap:
there are no repositories, Asset ownership integration, or authenticated
application entry points yet. It cannot truthfully enforce endpoint existence,
cross-Vault ownership, or global duplicates at runtime.

The resulting PR must therefore reference HV-13 without closing it. The full
story remains incomplete until the application/repository slices enforce and
test rules 3 and 5. Do not substitute fake repository checks or treat supplied
identifiers as verified ownership. If end-to-end completion is required now,
the scope must explicitly advance the application and storage-boundary work.

## Alternatives and tradeoffs

An arbitrary text kind allows more flexibility but leaves relationship semantics
undefined. Starting with Covers supplies the issue's concrete example. Physical
removal would require storage now and erase inspectable lifecycle information;
a Removed state keeps domain behavior testable but requires future queries to
exclude removed records explicitly. Allowing duplicate active links is simpler
but makes accidental repeated registration indistinguishable from intent.

## Tasks after acceptance

1. Implement the independent root, kind/state, creation result, and removal.
2. Test identity validation, unsupported kinds, self-links, exact references,
   removal retention/idempotency, and safe diagnostics without graph loading.
3. Document integration obligations and demonstrate fictional domain behavior.
4. Run restore, format verification, Release build, NUnit, Playground, and PR CI.

## Confirmation

The owner explicitly confirmed the rules and the limited domain delivery scope
on 2026-09-28. Existing aggregate boundaries and same-Vault rules remain accepted.
