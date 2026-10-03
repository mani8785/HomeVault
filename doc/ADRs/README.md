# Architecture Decision Records

## Process

Every architectural decision must be discussed with the user and recorded here. Start as Proposed; change to Accepted only after explicit confirmation. Record context, alternatives, rationale, consequences, and the decision date. Proposed decisions do not authorize implementation.

Use stable sequential IDs. Git history versions each document. Once accepted, preserve the historical decision: use a new ADR to supersede a changed decision, link both records, and mark the old record Superseded. Rejected proposals remain as history. Routine code details do not need individual ADRs unless they change architecture.

## Index

| ID | Decision | Status |
| --- | --- | --- |
| [0001](0001-initial-stack.md) | C# / .NET 10 and initial testing stack | Accepted 2026-09-25; NUnit initially confirmed 2026-09-23 |
| [0002](0002-domain-first-boundaries.md) | Domain-first DDD-lite and project boundaries | Accepted 2026-09-25; package restriction superseded by ADR-0005 |
| [0003](0003-selective-dependency-injection.md) | Selective dependency injection | Accepted 2026-09-23; package restriction superseded by ADR-0005 |
| [0004](0004-domain-language-and-boundaries.md) | Domain language and aggregate boundaries | Accepted 2026-09-28 |
| [0005](0005-guard-clauses.md) | Ardalis.GuardClauses input guards | Accepted 2026-09-28 |
| [0006](0006-asset-attributes.md) | Initial Asset attribute contract | Accepted 2026-09-28 |
| [0007](0007-sensitive-attributes.md) | Explicit sensitivity and deliberate attribute reads | Accepted 2026-09-28; partially supersedes ADR-0006 |
| [0008](0008-vault-creation.md) | Initial Vault creation contract | Accepted 2026-09-28 |
| [0009](0009-asset-relationships.md) | Initial Asset relationship contract | Accepted 2026-09-28 |
| [0010](0010-asset-evidence.md) | Initial Asset evidence contract | Accepted 2026-09-29 |
| [0011](0011-asset-reminders.md) | Initial Asset reminder contract | Accepted 2026-09-29 |
| [0012](0012-first-application-use-case.md) | Create a Vault for the current actor | Accepted 2026-09-29; transient success and synchronous API superseded by ADR-0014 |
| [0013](0013-vault-repository-contract.md) | Atomic Vault creation repository contract | Accepted 2026-10-01 |
| [0014](0014-in-memory-vault-infrastructure.md) | In-memory Vault storage and application integration | Accepted 2026-10-01 |
| [0015](0015-vault-bound-asset-registration.md) | Vault-bound Asset registration in Playground | Accepted 2026-10-01 |
| [0016](0016-nunit-5-upgrade.md) | NUnit 5 in both test projects | Accepted 2026-10-01; supersedes initial NUnit pin |

## Accepted persistence decision

[ADR-0017](0017-durable-local-persistence.md): SQLite with EF Core for durable local persistence, accepted 2026-10-01. Implementation slices are linked in the decision.

## Accepted encryption decision

[ADR-0018](0018-sensitive-value-encryption.md): Sensitive-value encryption,
key custody, recovery, rotation, and verification plan for HV-21. Accepted
2026-10-01; implementation slices #63 through #66 remain outstanding.

## Accepted browser UI decision

[ADR-0019](0019-browser-ui-first-journey.md): Angular with an independent ASP.NET
Core API, first Vault/Asset journey, and explicit HV-22 dependency boundary.
Accepted 2026-10-01. UI implementation is parked in Todo until HV-22 authentication
and authorization is addressed.

## Accepted authentication decision

[ADR-0020](0020-local-accounts-vault-authorization.md): invitation-only local
accounts, Identity/cookie recommendation, Vault permissions and staged operation
coverage for HV-22. Accepted 2026-10-01; #69 and #70 are merged. Tasks #71 and #72
track the remaining authorization delivery.

## Accepted operator workflow

[ADR-0021](0021-operator-invitations-recovery.md) specifies protected invitation
exports, recovery lifetime and offline operator actions for #70. Accepted
2026-10-02. See the [implementation guide](../hv-22-accounts.md).

## Accepted remaining-operation contracts

[ADR-0022](0022-remaining-authorized-operations.md) accepts durable Vault archive
and membership contracts, real-account targets and validated Domain restoration.
The [task plan](../hv-22-remaining-operations.md) splits #72 into reviewable slices.
Accepted 2026-10-02; ArchiveVault (#76) is the first authorized implementation.

## Accepted ordinary attribute contract

[ADR-0023](0023-durable-ordinary-attributes.md) accepts the HTTP operations,
ordinary-only schema, atomic writes and fail-closed sensitivity boundary for #78.
Accepted 2026-10-03; see the [implementation guide](../hv-22-ordinary-attributes.md).

## Proposed durable Evidence contract

[ADR-0024](0024-durable-url-note-evidence.md) proposes Asset-owned URL/Note
storage, separate metadata/content reads and exact HTTP/schema contracts for #79.
Plaintext content storage is an explicit review choice. Created 2026-10-03;
implementation awaits confirmation.

## Template

- Title and ID
- Status: Proposed / Accepted / Rejected / Superseded
- Created date; accepted date when applicable
- Context
- Decision proposal
- Alternatives considered
- Consequences and tradeoffs
- Discussion and confirmation
- References and supersession links
