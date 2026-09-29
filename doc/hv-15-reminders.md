# HV-15: Reminder domain operations

Issue: [#19](https://github.com/mani8785/HomeVault/issues/19).
Contract: accepted [ADR-0011](ADRs/0011-asset-reminders.md).

## Completed tasks

Reminder.Create validates non-empty Reminder/Vault/Asset identities and nonblank
action text in that order. A concrete result returns the complete Pending record
or a safe failure. Action text is preserved exactly and deliberately read through
ReadAction; default string and public-property JSON output omit it.

DueAt is an exact DateTimeOffset instant normalized to UTC. All representable
values, including past/default values, are allowed. The caller resolves local
timezone ambiguity. IsOverdue(now) consults no system clock: it is true only for
Pending records whose due instant is strictly earlier than now.

Update replaces action and due time atomically only while Pending. Complete and
Cancel transition to their corresponding terminal states. Repeating the same
terminal operation succeeds; switching terminal states or updating a terminal
record returns NotPending. Invalid updates preserve all state and references.

Tests cover validation order, exact text and references, offset normalization,
minimum/maximum dates, overdue equality and tick boundaries, past dates, atomic
updates, terminal behavior, and safe default serialization.

## Integration boundaries

No notifications, background scheduling, recurrence, date-only semantics,
persistence, or authentication are implemented. Application must later verify
actual Asset/Vault ownership, access, and archive state; supplied references do
not prove these facts. Persistence must enforce concurrency. ReadAction is not
authorization and its returned text must not be logged. Default JSON is not a
lossless storage representation. These integration obligations remain follow-up
work; this PR delivers only the explicitly approved domain scope.

## Terminal verification

Run each command from the repository root after the preceding succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

The no-build commands require a successful Release build. Playground uses a fixed
fictional due instant: creation succeeds, update returns None, a supplied later
instant is overdue, completion succeeds, and a further update returns NotPending.
Action text is not printed. No scripts or packages were added.
