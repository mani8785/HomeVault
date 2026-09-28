# ADR-0004: Domain language and aggregate boundaries

Status: Proposed
Created: 2026-09-25
Accepted: Not yet
Issue: [HV-04 / #8](https://github.com/mani8785/HomeVault/issues/8)

## Context

The accepted project boundaries in [ADR-0002](0002-domain-first-boundaries.md)
separate business rules from application orchestration and infrastructure. The
solution is scaffolded, but no domain behavior exists. The vocabulary and old
Vault rules in [context](../context.md) need concrete examples and confirmation
before the first domain types are implemented. This ADR supplements ADR-0002;
it does not change the accepted stack or selective dependency injection decision.

## Proposed vocabulary

All examples below are fictional and contain no credentials or account numbers.

| Term | Meaning | Example and distinction |
| --- | --- | --- |
| Asset | An independently identified record of something valuable or important. | A bicycle, an insurance policy, or a person record. A policy covering a bicycle is another Asset, not an attribute containing the entire policy. |
| Vault | The ownership and access boundary for records. | A household's shared records. Membership conveys access to records, not legal ownership of the bicycle. A Vault does not load all its Assets. |
| Asset attribute | A named piece of information maintained as part of one Asset. | A bicycle's frame material. Names, types, uniqueness, validation, and sensitivity rules are refined in HV-08/HV-09. |
| Relationship | An identified, directed association between two Assets. | An insurance-policy Asset covers a bicycle Asset. It references both identities rather than embedding either record. |
| Evidence | Supporting reference metadata attached to an Asset. | A receipt file reference, a policy URL, or a note. It does not imply that the domain stores file bytes or fetches a URL. |
| Reminder | An independently managed future action associated with an Asset. | Review the bicycle's insurance. A recorded reminder is not a scheduled notification; delivery channels and background jobs are separate concerns. |

A person represented as an Asset is distinct from an authenticated actor or
Vault member. Recording a person does not create an account or grant access.
Actor identity mapping remains an authentication decision under HV-22.

## Proposed aggregate boundaries

An aggregate is the unit whose own invariants must hold after every successful
mutation. References between aggregates use Guid identities, not loaded object
graphs. Do not add generic aggregate frameworks solely to express this proposal.

| Root | Owns and validates locally | References and coordination |
| --- | --- | --- |
| Vault | Its type, lifecycle, and memberships; member uniqueness and at least one Owner. | Member actor identifiers. Assets are queried separately by VaultId. |
| Asset | Its descriptive information, attributes, and Evidence reference metadata. | One VaultId. It does not own Vault membership, incoming/outgoing Relationship collections, or Reminder collections. |
| Relationship | Source/target identities and the association's own metadata. | One VaultId and two AssetIds; Application checks endpoint existence and ownership. |
| Reminder | Its action, due information, and lifecycle once defined in HV-15. | One VaultId and one AssetId; Application checks existence and ownership. |

Evidence is proposed as an Asset-owned component because it has no independent
lifecycle in the current stories. External files remain external resources;
removing a reference does not delete a file. Revisit this boundary if shared
Evidence or large independent collections become a concrete requirement.

Relationships and Reminders are proposed as separate roots so they can be
queried or changed without loading every association into an Asset or Vault.
Their detailed schemas and lifecycle rules remain later story decisions.

## Proposed consistency and ownership rules

1. A persisted Asset belongs to exactly one Vault. Cross-vault moves are not an
   initial operation. Registration validates the referenced Vault through the
   application boundary once Vault integration exists; early domain tests do not
   imply that authorization has been implemented.
2. Relationship endpoints and a Reminder's Asset must exist in the same Vault as
   that record. Reject cross-vault references even when an actor belongs to both
   Vaults. Missing and inaccessible targets must not reveal private information.
3. Domain operations preserve local invariants. Application checks actor access,
   Vault lifecycle, and cross-aggregate references; Infrastructure supplies the
   agreed persistence/concurrency mechanism. Domain does not query repositories
   or depend on an authentication provider to enforce its local rules.
4. Successful writes must not use stale authorization or lifecycle checks. A
   concurrent archive or membership change must serialize with the write or
   cause a conflict and revalidation before commit. A read-then-write check alone
   is insufficient. Choose the concrete transaction/version mechanism when
   repository contracts and persistence are designed; no database is selected here.
5. Read models may query by VaultId without loading a Vault's entire Asset set.
   Ownership/access checks still apply to reads. Do not use eventual consistency
   for the last-Owner rule, cross-vault isolation, or archive write protection.
6. Asset deletion and cascading behavior are deferred. Do not introduce hard
   deletion that could orphan Relationships or Reminders. When deletion is
   scoped, decide rejection, archival, or cleanup semantics before implementation.

## Historical Vault rules proposed for reconfirmation

- Types: Personal, Household, Organization. Type describes context, not a
  separate permission system. Proposed default: all three use the same role
  rules; Personal does not imply a hard one-member limit without confirmation.
- States: Active and Archived. Creation produces Active with an initial Owner
  atomically. Reject invalid identifiers and unsupported type values; detailed
  naming constraints are refined before constructors are implemented.
- Membership: one membership per actor per Vault. Mutations go through Vault;
  removing or demoting the last Owner is rejected without partial changes.
- Roles: Owner, Administrator, Editor, Viewer, with the proposed matrix below.
- Archival: only an Owner can transition Active to Archived. Existing authorized
  members may still read. All record and membership mutations are blocked after
  archival, including attributes, Evidence, Relationships, and Reminders.
  Repeating archive is proposed as a no-op after access checks. Reactivation,
  permanent deletion, and emergency membership recovery are out of initial scope.
  The inability to revoke membership in an archived Vault needs explicit review.

| Operation on an Active Vault | Owner | Administrator | Editor | Viewer |
| --- | --- | --- | --- | --- |
| Read permitted records | Yes | Yes | Yes | Yes |
| Create/change records and supporting information | Yes | Yes | Yes | No |
| Add/remove Editors or Viewers; switch between those roles | Yes | Yes | No | No |
| Grant/revoke Administrator or Owner roles | Yes, preserving an Owner | No | No | No |
| Change Vault metadata or archive | Yes | No | No | No |

An Administrator cannot promote themselves or alter another Administrator or
Owner. This is a proposed operation matrix, not implemented authorization.
Sensitive-value access must be refined in HV-09/HV-22; ordinary read permission
must not be interpreted as approval to expose every sensitive value.

## Review examples and future acceptance scenarios

| Scenario | Proposed outcome |
| --- | --- |
| Create a Household Vault with an initial Owner. | Active Vault with exactly the supplied initial membership; no Assets need to be loaded. |
| Add a second membership for the same actor. | Reject; existing membership remains unchanged. |
| Demote the only Owner, or remove them. | Reject; at least one Owner remains. |
| Link a policy in Vault A to a bicycle in Vault B. | Reject even if the caller can access both Vaults. |
| Attach a receipt reference to a bicycle. | Update that Asset's Evidence metadata; no file upload or network access is implied. |
| Schedule a reminder record for a bicycle. | Store action/due information once its contract is defined; do not send a notification. |
| Archive a Vault, then edit an Asset or remove a member. | Both writes are rejected; permitted reads remain available. |
| Archive races with an Asset update. | Serialize the operations or reject/revalidate the stale update; a write cannot commit after archive based on an earlier Active check. |

These are review scenarios for future tests, not claims that these features or
behavioral tests exist today. Current NUnit tests cover scaffold architecture.

## Alternatives and consequences

A Vault containing all Assets would make unrelated edits compete on one large
aggregate and require unnecessary loading. Asset roots keep record changes
local, while making access/lifecycle consistency an explicit application concern.
Embedding Relationships and Reminders in Asset is initially simpler but makes
independent queries and lifecycles harder. Separate roots add coordination cost;
revisit if real usage does not justify it. Evidence stays within Asset initially.

The proposed strict archive behavior preserves the historical no-mutation rule
but prevents membership recovery. Allowing Owner-only recovery would be a
reasonable alternative, requiring an explicit exception and dedicated tests.

## Review checklist and remaining decisions

The following decisions require owner confirmation before this ADR is Accepted:

- [ ] Confirm the six definitions and the distinction between a person Asset and an actor.
- [ ] Confirm the root/component boundaries and same-Vault reference rules.
- [ ] Confirm all three Vault types, common role rules, the operation matrix, and last-Owner protection.
- [ ] Confirm strict archive behavior, retained reads, repeated archive as a no-op, and deferred recovery/reactivation.

Later stories refine Asset naming and required fields (HV-07), attribute rules
and sensitivity (HV-08/HV-09), Relationship kinds/self-links/duplicates (HV-13),
Evidence reference formats and limits (HV-14), Reminder date/time-zone/lifecycle
semantics (HV-15), and persistence concurrency enforcement (HV-17/HV-20).
These are explicit boundaries on this proposal, not silently chosen defaults.

HV-04 has no linked subtasks. Its small deliverable consists of this vocabulary,
boundary proposal, and confirmation checklist; duplicate sub-issues are unnecessary.
The request authorized preparing and delivering this proposal through a PR, not
recording new product decisions as already accepted. Keep #8 open until review
resolves the checklist. After acceptance, implement only the next authorized
slice; do not scaffold generic domain types or advance automatically to HV-05.
