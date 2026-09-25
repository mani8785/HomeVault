# Architecture Decision Records

## Process

Every architectural decision must be discussed with the user and recorded here. Start as Proposed; change to Accepted only after explicit confirmation. Record context, alternatives, rationale, consequences, and the decision date. Proposed decisions do not authorize implementation.

Use stable sequential IDs. Git history versions each document. Once accepted, preserve the historical decision: use a new ADR to supersede a changed decision, link both records, and mark the old record Superseded. Rejected proposals remain as history. Routine code details do not need individual ADRs unless they change architecture.

## Index

| ID | Decision | Status |
| --- | --- | --- |
| [0001](0001-initial-stack.md) | C# / .NET 10 and initial testing stack | Accepted 2026-09-25; NUnit initially confirmed 2026-09-23 |
| [0002](0002-domain-first-boundaries.md) | Domain-first DDD-lite and project boundaries | Accepted 2026-09-25 |
| [0003](0003-selective-dependency-injection.md) | Selective dependency injection | Accepted 2026-09-23 |
| [0004](0004-domain-language-and-boundaries.md) | Domain language and aggregate boundaries | Proposed; HV-04 review pending |

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
