# ADR-0003: Selective dependency injection

Status: Accepted
Created: 2026-09-23
Accepted: 2026-09-23

## Context

HomeVault needs explicit dependencies and testable use cases while keeping its domain model small and independent of frameworks. Dependency injection should clarify collaboration between components without introducing abstractions for every type.

## Decision

Use constructor injection where a component needs a collaborator and making that dependency explicit improves clarity or testing. Introduce interfaces only for concrete boundaries or substitution needs. Application owns contracts for technical capabilities required by use cases; Infrastructure supplies implementations.

Keep Domain dependent only on .NET base libraries. Domain must not reference DI packages, containers, registration APIs, or framework attributes. If a domain service needs a collaborator, express it using ordinary C# and a domain-owned contract only when justified. Construct entities and value objects through their domain constructors or factories, not through a DI container.

Wire concrete implementations in the executable composition root, initially Playground. Prefer manual construction while it is clear. Consider a DI container only when wiring complexity justifies it, with the package and lifetime policy reviewed in a subsequent ADR before adoption. Do not resolve dependencies through a service locator or pass IServiceProvider into Domain or Application.

## Alternatives and rationale

Constructing technical collaborators inside use cases hides dependencies and makes substitution harder. A container from the outset adds a package and lifetime conventions before a demonstrated need. Constructor injection exposes required collaborators while allowing simple manual composition.

## Consequences

Dependencies become visible and tests can supply focused substitutes. Composition requires explicit wiring. Interfaces and a container are not mandatory for every class. The existing project dependency direction remains unchanged, and no DI framework is selected.

## Discussion and confirmation

On 2026-09-23, the user confirmed adding dependency injection where it improves clarity while preserving Domain's independence from frameworks, and requested this decision be documented. Acceptance records that direction; implementation remains gated by confirmation of each subsequent small step. This step changes documentation only.

## Related documentation

- [Architecture](../architecture.md)
- [ADR-0002: Domain-first boundaries](0002-domain-first-boundaries.md)
- [Development and review workflow](../workflow.md)
