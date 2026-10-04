# HV-22.4.6: Durable Reminder lifecycle

Implements [#81](https://github.com/mani8785/HomeVault/issues/81) under
[ADR-0026](ADRs/0026-durable-reminder-lifecycle.md), accepted 2026-10-04.

## User behavior

Create a Reminder for an actual same-Vault Asset, inspect its metadata, deliberately
read its action, update both Pending fields, or complete/cancel it. Completed and
Cancelled roots remain readable; the same terminal operation may be repeated,
but switching terminal state or updating a terminal root returns 409 not_pending.
There is no scheduling, notification, recurrence, reopening, deletion or UI.

Owner, Administrator and Editor can write in Active Vaults; Viewer cannot write.
Every operation checks current membership. Archived Vaults remain readable to
members, but every write (including terminal repeats) is rejected. Membership in
two Vaults does not authorize cross-Vault Asset references. Missing/inaccessible
resources share a safe 404 response. All writes require antiforgery and authenticated
cookies; caller actor fields are rejected.

Action text is private and preserved exactly, but plaintext in the database and
backups. GET metadata and creation responses omit it. GET /action deliberately
returns it; avoid logging that response. This is not Sensitive-value encryption.

## Implementation and database

ReminderUseCases reads trusted identity once and invokes IReminderStore.
SqliteReminderStore starts a non-deferred write transaction before current access
checks, verifies actual Asset ownership, restores validated Domain state and
invokes Create/Update/Complete/Cancel before saving. Read transactions give a
consistent view of access and state. Concurrent transitions serialize; complete
versus cancel has one winner, and competing Pending edits have last-commit-wins
semantics without an ETag. Failed validation leaves both fields unchanged.

Migration `20261004145533_AddReminders` adds a separate Reminders table with
restricted Vault/Asset foreign keys, nonempty identity and lifecycle checks.
DueAtUtcTicks is an INTEGER constrained to the entire .NET UTC tick range, including
zero. It retains 100 ns precision, unlike Unix milliseconds. The adapter checks
same-Vault ownership; direct external SQL and Asset movement are unsupported.
Invalid stored state/ownership fails safely rather than exposing inconsistent data.

HTTP dueAt requires seconds and an explicit Z or numeric offset, with at most
seven fractional digits. Offsetless/date-only values and precision loss are
rejected. Output is UTC with seven fractional digits and Z. Past instants and
years 0001 through 9999 remain valid. Domain never reads the system clock.

Existing EF Core, SQLite, ASP.NET Identity/antiforgery and approved input guards
cover this slice. No package or script was added. See [OpenAPI 1.6.0](openapi/homevault-v1.json).

## Validate from PowerShell

Use a clean working tree and run commands individually, stopping on failure:

```powershell
cd D:\repos\HomeVault
git fetch origin
git switch codex/hv-22-4-6-reminder-contract
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/reminders
dotnet ef migrations has-pending-model-changes --project src/HomeVault.Infrastructure --configuration Release --no-build
```

Tests cover restoration/privacy, trusted identity, UTC precision/range, lifecycle,
same-Vault checks, real cookie authentication, all roles, revocation, archive,
permission changes after authentication, atomic contention, rollback, schema
constraints, restart, upgrade and verified backup/restore. Build should report no
warnings; tests should pass; the EF command should report no pending model changes.

## Upgrade and run

Complete [account setup](hv-22-accounts.md), including private directories, session
keys and HTTPS certificate. Build successfully before `--no-build`. Stop the API
and other writers, back up the existing database to a new path, then migrate
explicitly. Startup never migrates automatically.

```powershell
$accountHome = "$env:LOCALAPPDATA/HomeVault-Accounts"
$accountDatabase = "$accountHome/accounts.db"
$accountKeys = "$accountHome/session-keys"
$backupPath = "$accountHome/before-reminders-$([Guid]::NewGuid().ToString('N')).db"
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage backup $accountDatabase $backupPath
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage migrate $accountDatabase
dotnet run --project src/HomeVault.Api -c Release --no-build -- serve $accountDatabase $accountKeys
```

The API listens on `https://localhost:7443`; stop with Ctrl+C. Keep a known-good
original and follow [verified recovery](hv-20-persistence.md) plus the
[account restore precautions](hv-22-accounts.md) when restoring a backup.

In a second terminal, follow the [authenticated journey](hv-22-authorized-api.md)
to obtain `$origin`, `$session`, refreshed `$csrf`, `$vault` and a fictional `$asset`.

```powershell
$reminders = "$origin/api/v1/vaults/$($vault.id)/reminders"
$reminderId = [Guid]::NewGuid().ToString('D')
$body = @{ id = $reminderId; assetId = $asset.id; action = 'Review fictional insurance'; dueAt = '2030-01-02T03:04:05.1234567+02:30' } | ConvertTo-Json
Invoke-WebRequest $reminders -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body $body
$entry = "$reminders/$reminderId"
Invoke-RestMethod $entry -WebSession $session
Invoke-RestMethod "$entry/action" -WebSession $session
$update = @{ action = 'Review fictional renewal'; dueAt = '2030-02-01T12:00:00Z' } | ConvertTo-Json
Invoke-WebRequest $entry -Method Put -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body $update
Invoke-WebRequest "$entry/complete" -Method Post -WebSession $session -Headers @{'X-XSRF-TOKEN'=$csrf}
Invoke-RestMethod $entry -WebSession $session
```

Expected: creation 201 with Location, exact UTC metadata, deliberate fictional
action read, update 204, complete 204, then completed metadata. Repeating complete
succeeds while authorized and Active. Calling cancel on that root returns 409;
create a new Reminder to demonstrate cancellation. No requests are automatically
retried and no notification is sent. Keep real action text and cookies out of logs.

## Parent-story coverage

PRs #83–#87 delivered archive, membership, ordinary attributes, Evidence and
Relationships. This PR adds the remaining ordinary Reminder operations. #72 and
#26 stay open: Sensitive APIs remain unexposed pending explicit access policy and
HV-21 encryption/key recovery (#63–#66). The existing role/isolation tests cover
the delivered operations, not those deferred Sensitive capabilities. File/document
resolution, notifications and the parked Angular UI remain separate backlog work.
