# ADR-0001: Initial language, runtime, and testing stack

Status: Proposed (NUnit choice confirmed; remaining stack choices await confirmation)
Created: 2026-09-23
Accepted: NUnit choice only, 2026-09-23; full ADR not yet accepted

## Context

HomeVault is a fresh C#/.NET restart. The local machine has SDK 10.0.300. The user requested discussion of the stack before implementation.

## Decision proposal

Use C# with .NET 10 LTS, nullable reference types, and the stable language version supplied by the SDK. Pin the agreed SDK policy with global.json during scaffolding. Use NUnit for automated tests (confirmed), with NUnit's own assertions proposed initially, selecting and recording compatible package versions at scaffolding. Use a console Playground before selecting a UI. Avoid additional packages until justified.

## Alternatives

.NET 8/9 have shorter remaining support; preview runtimes add unnecessary change. xUnit and MSTest are alternatives to NUnit. NUnit replaces the earlier xUnit proposal because the user explicitly selected it. A separate assertion library is unnecessary initially.

## Consequences

The installed SDK supports starting immediately. An LTS baseline reduces upgrade pressure. NUnit adds a test-only dependency. Database, ORM, UI, and hosting remain open decisions.

## Discussion and confirmation

On 2026-09-23, the user explicitly confirmed replacing the proposed xUnit choice with NUnit and requested that confirmation be recorded here. This confirms the test framework only; the other stack proposals remain subject to review. Package versions and test tooling will be reviewed during a separately confirmed scaffolding step. No code is scaffolded in this documentation step.

## References

https://dotnet.microsoft.com/en-us/platform/support/policy
