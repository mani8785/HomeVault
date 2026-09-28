# ADR-0005: Use Ardalis.GuardClauses for input guards

Status: Accepted
Created: 2026-09-28
Accepted: 2026-09-28
Supersedes in part: [ADR-0002](0002-domain-first-boundaries.md) and [ADR-0003](0003-selective-dependency-injection.md), only their base-library-only Domain restriction and manual input-guard preference.

## Context and confirmation

During review of PR #35, the owner explicitly requested Ardalis.GuardClauses in
place of handwritten guard checks, and asked that repository and skill guidance
remember this preference. That request authorizes this narrowly scoped dependency
exception; no additional approval is inferred for other frameworks.

## Decision

Reference Ardalis.GuardClauses 5.0.0 in Domain. Prefer its built-in guards for
standard null, empty, whitespace, range, and similar preconditions when their
semantics match the approved contract. Keep business invariants explicit and
add custom guard extensions only for a demonstrated reusable rule.

Asset's private constructor uses Guard.Against.NullOrEmpty for its Guid and
Guard.Against.NullOrWhiteSpace for its name, in that order. These guards throw
ArgumentException (including ArgumentNullException). Asset.Create catches only
argument failures with the expected parameter names and maps them to existing
AssetCreationError values. Exception messages and caller values are neither
logged nor returned. Other unexpected exceptions propagate.

The public factory keeps its existing nonthrowing validation-result contract,
identity-first precedence, and exact preservation of valid names. No broad
validation framework or generic result adapter is introduced. Guard types do
not appear in the public domain API.

## Alternatives and consequences

Handwritten conditions avoid a dependency and exception overhead but do not use
the owner's chosen guard library. Letting guard exceptions escape would simplify
the factory but break its agreed result contract. The selected adapter preserves
that contract at the cost of exception allocation on invalid input; this is not
necessarily fewer lines for a result-returning factory. Avoid broad exception
catching or exposing guard messages in future operations.

Domain now permits this one approved utility package. The architecture test
continues rejecting other package, project, framework, and external assembly
references. Persistence, UI, DI containers, and event dispatch remain excluded.
Keep the installed version in the project file and review upgrades normally.

## Sources and validation

- [GuardClauses usage and extension documentation](https://github.com/ardalis/GuardClauses)
- [NuGet package 5.0.0](https://www.nuget.org/packages/Ardalis.GuardClauses/5.0.0)

Existing Asset creation tests exercise all supported valid/invalid inputs,
including null, Unicode whitespace, error precedence, and safe failure output.
They must continue passing without changing expected behavior.
