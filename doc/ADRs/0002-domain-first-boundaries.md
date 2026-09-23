# ADR-0002: Domain-first DDD-lite and project boundaries

Status: Proposed
Created: 2026-09-23
Accepted: Not yet for this restart

## Context

The previous project intentionally moved beyond a CRUD-first inventory. The fresh implementation should preserve meaningful domain behavior without unnecessary abstractions.

## Decision proposal

Use Domain, Application, Infrastructure, Playground, and Tests projects with dependencies shown in the [architecture](../architecture.md). Domain uses only base libraries and owns business rules. Application expresses use cases. Infrastructure implements technical capabilities. Use Guid IDs, manual validation, and lightweight project-owned patterns where needed. Model domain events using plain C# types. Decide how to dispatch and handle them when a use case requires it; no event-dispatch library is selected yet. Do not introduce MediatR solely for domain events. Sensitive-value modeling belongs in Domain; encryption and persistence belong in Infrastructure. Apply the confirmed [selective dependency injection decision](0003-selective-dependency-injection.md) without introducing framework dependencies into Domain.

## Alternatives

A CRUD-first design is simpler for basic records but does not reflect the intended domain behavior. Full DDD/CQRS infrastructure and generic repository frameworks would add complexity before a demonstrated need. A single project reduces setup but weakens enforceable boundaries.

## Consequences

Project references make dependency direction explicit. More projects add setup overhead. Implement building blocks only as needed and review aggregate boundaries with real use cases. No persistence or UI framework is selected by this ADR.

## Discussion and confirmation

Reflects prior user requirements; awaiting confirmation for the fresh implementation.
