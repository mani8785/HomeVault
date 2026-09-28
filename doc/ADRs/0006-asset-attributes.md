# ADR-0006: Initial Asset attribute contract

Status: Accepted
Partially superseded by [ADR-0007](0007-sensitive-attributes.md) for attribute
Value access and explicit classification on add; other rules remain accepted.
Created: 2026-09-28
Accepted: 2026-09-28
Issue: [HV-08 / #12](https://github.com/mani8785/HomeVault/issues/12)

## Context

[ADR-0004](0004-domain-language-and-boundaries.md) makes attributes Asset-owned
information and explicitly defers names, types, uniqueness, and mutation rules
to HV-08. The issue has no linked subtasks or discussion settling these rules.
The existing Asset supports creation and inspection with a Guid and name.

## Proposed contract

1. Start with text values only. Attribute names and values must be nonblank.
   Trim names before storing or matching; preserve values exactly. Do not impose
   arbitrary length limits or parse dates, numbers, units, or URLs in this slice.
2. Match names within one Asset using ordinal, case-insensitive comparison.
   For example, `Material`, `material`, and ` Material ` identify the same
   attribute. Preserve the trimmed original spelling for display.
3. Add rejects an existing name with `DuplicateName`. Change replaces only an
   existing attribute's value; it does not rename or create an attribute.
   Change and remove reject missing names with `NotFound`. Replacing a value
   with the identical value succeeds without changing observable state.
4. Validate the name before the value, and both before duplicate/missing checks
   for add/change. Return `BlankName` or `BlankValue` as appropriate. A failed
   operation leaves every existing attribute unchanged.
5. Asset owns add/change/remove operations. Expose immutable attribute entries
   in a read-only snapshot so callers cannot bypass validation; ordering is not
   a business guarantee. Attributes have no separate Guid or independent life.
6. Use the approved Ardalis.GuardClauses guards for input preconditions and
   project-owned error/results. Neither diagnostics nor default string output
   includes attribute names or values. Deliberate property reads remain possible.

Examples: a bicycle can carry `Material = Steel`; an insurance policy can carry
`Provider = Example insurer`. Neither example introduces a category schema.
Removing an attribute removes only its entry from the current Asset instance.

## Alternatives and tradeoffs

Text keeps this first operation small but does not provide numeric/date
validation, sorting, units, or typed comparisons. A discriminated value model
could provide those features after supported types and conversion semantics
are agreed. Case-sensitive names preserve exact distinctions but allow confusing
duplicates. Upsert and idempotent removal are convenient but hide missing-name
mistakes; explicit errors make the first mutation contract easier to inspect.

## Scope and implementation tasks after acceptance

1. Add validated immutable attribute entries and safe mutation results.
2. Implement Asset add/change/remove and snapshot inspection sequentially.
3. Test normalization, duplicate matching, invalid inputs, missing entries,
   successful changes/removal, failure atomicity, snapshot isolation, and safe
   diagnostics. Extend the fictional Playground scenario and user documentation.
4. Run restore, formatting verification, Release build, NUnit tests, and the
   Playground; publish the implementation PR for owner review.

Sensitivity/access policy remains HV-09/HV-22. This slice does not establish
permission to store credentials. Persistence, encryption, Vault checks,
attribute renaming, typed values, events, and generic DDD bases are outside scope.
No new dependency, script, storage adapter, or application service is required.

## Confirmation

The owner explicitly accepted the six contract items in this task on
2026-09-28. This decision does not change the accepted aggregate or project boundaries.
