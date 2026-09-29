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
