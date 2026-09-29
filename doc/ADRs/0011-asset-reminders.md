# ADR-0011: Initial Asset reminder contract

Status: Accepted
Created: 2026-09-29
Accepted: 2026-09-29
Issue: [HV-15 / #19](https://github.com/mani8785/HomeVault/issues/19)

## Context

[ADR-0004](0004-domain-language-and-boundaries.md) defines Reminder as an
independent root referencing one Vault and one Asset. It defers action fields,
time semantics, and lifecycle to HV-15. A reminder record does not schedule or
send notifications. The issue has no discussion or linked subtasks resolving
these decisions.

## Proposed contract

1. Create with non-empty Reminder, Vault, and Asset Guids, a nonblank action
   string preserved exactly, and a required DateTimeOffset dueAt. References
   remain identities without loading an Asset graph. Do not impose arbitrary
   action length, uniqueness, or normalization rules.
2. dueAt represents an exact instant. Normalize it to UTC for storage; expose
   DateTimeOffset in UTC. The caller must resolve local dates/times to an offset
   before calling Domain. Do not infer a zone, use the machine's local zone, or
   retain a named timezone. An offset is not a timezone and cannot define future
   recurring daylight-saving rules. Date-only and recurring reminders are deferred.
3. Permit past due instants so overdue or imported actions can be recorded.
   Accept the full representable DateTimeOffset range, including its default
   value as a valid instant; do not introduce a sentinel for missing dates.
   IsOverdue(now) is true only while Pending and dueAt is strictly before the
   supplied instant. Equality is not overdue. Never read the system clock in Domain.
4. Create in Pending state. Update(action, dueAt) changes both fields atomically
   only while Pending; identity and references never change. Invalid actions
   leave state untouched. Complete transitions Pending to Completed; Cancel
   transitions Pending to Cancelled. Repeating the same terminal operation
   succeeds unchanged, but switching terminal states or updating a terminal
   reminder returns NotPending. No reopen or delete operation is included.
5. Validate creation in input order: Reminder id, Vault id, Asset id, action.
   Return a concrete creation result with no partially valid Reminder on failure.
   Use safe EmptyIdentity, EmptyVaultIdentity, EmptyAssetIdentity, and BlankAction
   errors. Update checks Pending state before validating action. Use approved
   guards for standard inputs; do not echo supplied text or exception messages.
6. Action text may be private: keep it in a private field with a deliberate
   ReadAction() method. Default string formatting and public-property JSON must
   not expose it. This is an accidental-disclosure safeguard, not authorization.
   Metadata and references remain deliberately inspectable. Examples use only
   fictional inputs and print outcomes/state without action text.

## Integration and delivery scope

Implement the domain record, lifecycle, overdue calculation, tests, documentation,
and Playground in this step. Application must later resolve actual Asset/Vault
ownership, authenticate and authorize the caller, and block writes to Archived
Vaults; persistence must enforce the accepted concurrency rules. Supplied IDs
alone prove neither existence nor ownership. Do not claim those protections here.

No notification transport, background scheduler, recurrence, database, clock
service, or timezone library is introduced. The PR delivers the agreed domain
operations; same-Vault/access/archive integration remains an explicit follow-up
and must be visible in the handoff rather than implied by domain test success.

## Alternatives and tradeoffs

A DateOnly deadline avoids timezones but cannot represent a precise action time.
A local date/time plus named zone supports wall-clock intent but requires rules
for ambiguous/nonexistent DST times and timezone changes. UTC instants give a
small deterministic first contract while leaving those choices explicit for
future recurrence or scheduling. Rejecting past dates would prevent recording
existing overdue tasks and would require a clock-dependent validation policy.

## Tasks after acceptance

1. Implement creation, immutable references, UTC due time, and safe action access.
2. Implement atomic Pending updates, completion, cancellation, and IsOverdue.
3. Test validation/precedence, offsets and UTC normalization, date boundaries,
   exact due equality, past dates, lifecycle transitions, atomic failures, and
   safe diagnostics/default serialization.
4. Update Playground and documentation; run restore, formatting verification,
   Release build, NUnit tests, smoke test, and PR CI.

## Confirmation

The owner explicitly confirmed these fields, time semantics, lifecycle, and
domain-only scope on 2026-09-29. Existing aggregate and ownership boundaries remain accepted.
