# ADR-0002: Domain-first DDD-lite and project boundaries

Status: Accepted
Created: 2026-09-23
Accepted: 2026-09-25

## Context

The previous project intentionally moved beyond a CRUD-first inventory. The fresh implementation should preserve meaningful domain behavior without unnecessary abstractions.

## Decision

Use Domain, Application, Infrastructure, Playground, and Tests projects with dependencies shown in the [architecture](../architecture.md). Domain uses only base libraries and owns business rules. Application expresses use cases. Infrastructure implements technical capabilities. Use Guid IDs, manual validation, and lightweight project-owned patterns where needed. Model domain events using plain C# types. Decide how to dispatch and handle them when a use case requires it; no event-dispatch library is selected yet. Do not introduce MediatR solely for domain events. Sensitive-value modeling belongs in Domain; encryption and persistence belong in Infrastructure. Apply the confirmed [selective dependency injection decision](0003-selective-dependency-injection.md) without introducing framework dependencies into Domain.

## Alternatives

A CRUD-first design is simpler for basic records but does not reflect the intended domain behavior. Full DDD/CQRS infrastructure and generic repository frameworks would add complexity before a demonstrated need. A single project reduces setup but weakens enforceable boundaries.

## Consequences

Project references make dependency direction explicit. More projects add setup overhead. Implement building blocks only as needed and review aggregate boundaries with real use cases. No persistence or UI framework is selected by this ADR.

## Discussion and confirmation

The owner accepted the concrete specification on 2026-09-25 and authorized HV-03 scaffolding on a feature branch, delivered through a pull request.

## HV-02 project specification (2026-09-25)

Use one root `HomeVault.slnx`. These paths and direct references make the
accepted architecture concrete for the existing scaffolding issue HV-03:

| Project path | Kind | Direct project references |
| --- | --- | --- |
| `src/HomeVault.Domain/HomeVault.Domain.csproj` | Class library | None |
| `src/HomeVault.Application/HomeVault.Application.csproj` | Class library | Domain |
| `src/HomeVault.Infrastructure/HomeVault.Infrastructure.csproj` | Class library | Application, Domain |
| `src/HomeVault.Playground/HomeVault.Playground.csproj` | Console | Application, Infrastructure, Domain |
| `tests/HomeVault.Tests/HomeVault.Tests.csproj` | NUnit tests | Domain, Application |

Apply [ADR-0001](0001-initial-stack.md) consistently. Use root
`Directory.Build.props` for shared framework/compiler settings, enabling XML
output explicitly in production projects. No reverse references or production
references to Tests are permitted. Empty layers need no marker types, generic
entities, result frameworks, repositories, or invented domain rules merely to
populate the scaffold.

Application owns technical contracts when a use case needs them. Infrastructure
implements those contracts, and Playground composes collaborators manually.
Revisit test references when adapter integration tests are introduced. Accepted
[ADR-0003](0003-selective-dependency-injection.md) is unchanged. Database, ORM,
UI, hosting, authentication, encryption, and event dispatch remain deferred.

### Ordered handoff to HV-03

HV-02 has no linked sub-issues as checked on 2026-09-25. Its existing acceptance
criteria are sufficient for this small decision update; no duplicate issues are
needed. [HV-03 / #7](https://github.com/mani8785/HomeVault/issues/7) already tracks
scaffolding. The owner authorized these tasks on 2026-09-25, in this order:

1. Create the root solution, five projects, and agreed SDK/compiler settings.
2. Wire the references and pinned test packages; check dependency direction.
3. Add meaningful tests for approved behavior or enforceable architecture constraints, never an always-passing placeholder.
4. Run restore, formatting verification, Release build, and NUnit tests. Verify actual executed tests in TRX and the public XML diagnostics.

This is the accepted implementation contract for HV-03. The resulting scaffold is delivered for review through a pull request; subsequent domain implementation and merging remain separate steps.
