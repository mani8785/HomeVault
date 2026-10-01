# ADR-0016: NUnit 5 in both test projects

Status: Accepted
Created: 2026-10-01
Accepted: 2026-10-01

## Decision and confirmation

The owner explicitly approved migrating both test projects to NUnit 5.0.0 and
reviewing async assertions after the failure in PR #47 was explained. This
supersedes only the NUnit 4.6.1 pin in [ADR-0001](0001-initial-stack.md).
Keep NUnit3TestAdapter 6.3.0, the existing test SDK, VSTest, and TRX reporting.

## Context and consequences

The dependency PR initially upgraded only HomeVault.Tests. NUnit 5 async
exception assertions return tasks: comparing an unawaited CatchAsync result
compared a task with the expected exception. Both test projects now use 5.0.0;
all ThrowsAsync and CatchAsync calls are awaited, and their containing tests
return Task. Test intent and production behavior remain unchanged.

Staying on NUnit 4 would avoid migration but leave the requested upgrade undone.
Upgrading only one project creates inconsistent assertion semantics. Validate
the coordinated upgrade with restore, formatting, Release build, all executed
tests, and PR CI. No runner migration or additional package is introduced.
