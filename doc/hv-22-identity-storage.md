# HV-22.1: Identity storage and protected session keys

Implements [task #69](https://github.com/mani8785/HomeVault/issues/69) under
accepted [ADR-0020](ADRs/0020-local-accounts-vault-authorization.md).

## Account storage

Infrastructure now owns HomeVaultUser and ASP.NET Core Identity's EF store model,
using Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.12 alongside the
existing EF 10.0.12 packages. The ASP.NET Core shared framework is referenced only
from Infrastructure and flows to composition/test hosts; Domain and Application
remain independent. No login, invitation, recovery, or HTTP endpoint exists yet.

AddIdentityAccounts adds the standard Identity tables plus account IsEnabled and
a nonempty Guid constraint. HomeVaultUser allocates a Guid and security stamp;
Identity UserManager hashes passwords. A raw storage entity is not an API DTO and
must never be serialized or logged. IsEnabled is storage for the next slice to
enforce, not an authentication check on its own. Identity's global role tables do
not grant Vault roles. Membership permissions remain in Memberships.

Existing Vault/Asset tables, their data, and fictional actor IDs are preserved.
No accounts or memberships are seeded, and existing fictional memberships are not
claimed by new users. Use a fresh account-enabled database for the later account
workflow. Upgrade tests preserve a populated AddAssets schema; backup/restore tests
verify account identity and framework password-hash verification after reopening.
The migration snapshot must match the current model. Do not use Down to recover
an account database: preserve and restore a verified backup instead.

## Windows session-key boundary

WindowsSessionKeys implements IDataProtectionProvider for eventual host composition.
It uses ASP.NET Core Data Protection and DPAPI CurrentUser. Initialize provisions a
new ring with a current-user-only directory ACL in a staging directory, then moves
it into a new destination without overwriting. The parent must already exist.
Each newly generated key file receives an explicit current-user owner and private
ACL, including when provisioning or renewal runs from an elevated Windows process.
Existing ring permissions are validated, never silently repaired.
Open requires an existing ring, validates owner/permissions and protected XML,
decrypts all non-revoked keys, and requires an active unexpired key. Missing,
corrupt, plaintext, inaccessible or expired rings fail safely without replacement.

Automatic key generation is disabled. Each explicitly created key has a 90-day
lifetime. Stop hosts and run Renew before expiry, then restart: it retains all old
keys and can recover an otherwise valid expired ring. Never delete keys still
needed by outstanding cookies/tokens. Open providers can cache keys; this slice
checks ring validity at startup, not on every subsequent cryptographic operation.
The future host must register the opened provider and dispose it at shutdown.

Paths must be absolute, outside Git checkouts, and free of existing reparse-point
ancestors. The directory and XML files must be owned by the current Windows user
and grant no access to other principals; inherited directory permissions are
rejected. These are local-account controls, not protection from administrators or
malware running as the same user. Non-Windows key operations fail explicitly.
Filesystem/crypto configuration failures expose constant messages without paths,
key content or inner exceptions. Provisioning creates no console/file logging.

Session keys are separate from the parked encryption of Sensitive Asset values.
A SQLite backup contains Identity records but does not include the external ring.
DPAPI-protected keys are tied to the Windows user context and are not a portable
recovery export. Key loss invalidates existing sessions/tokens; it does not destroy
Asset data or password hashes. Before any restored identity database is reopened
for users, the next account/recovery slice must implement the accepted invalidation
of all old sessions and invitations. This foundation does not provide that workflow.

## Terminal validation and fictional demonstration

Run these commands one at a time from a clean checkout and stop on failures:

```powershell
git switch codex/hv-22-1-identity-storage
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/identity
dotnet ef migrations has-pending-model-changes --project src/HomeVault.Infrastructure --configuration Release --no-build
```

On Windows, choose fresh fictional paths outside the checkout. These commands
provision session keys, verify them in another process, and explicitly renew them.
No passwords, tokens, or key contents are passed as arguments or printed:

```powershell
New-Item -ItemType Directory -Force "$env:LOCALAPPDATA/HomeVault"
$sessionKeyPath = "$env:LOCALAPPDATA/HomeVault/demo-session-keys"
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- session-keys initialize $sessionKeyPath
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- session-keys check $sessionKeyPath
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- session-keys renew $sessionKeyPath
```

Expect `Session key configuration verified for the current Windows user.`
Initialize refuses an existing destination; use check on subsequent runs.
Migration remains an explicit separate operation with an existing parent directory:

```powershell
$databasePath = "$env:LOCALAPPDATA/HomeVault/identity-demo.db"
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- storage migrate $databasePath
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- storage create $databasePath
dotnet run --project src/HomeVault.Playground --configuration Release --no-build -- storage read $databasePath
```

Create requires a fresh demonstration database. No accounts are enrolled by these
commands. For existing files, stop writers and follow the
[backup-before-upgrade procedure](hv-20-persistence.md).

## Verification scope

NUnit tests cover actual Windows DPAPI persistence/reopening and purpose isolation,
renewal retaining old payloads, missing/corrupt/plaintext/expired keys, unsafe file
and directory ACLs, invalid paths and explicit unsupported-platform behavior.
No cross-user impersonation is performed; wrong-user handling relies on DPAPI and
the owner check and requires the later enrollment/recovery integration scenarios.
Linux runs the platform-rejection test; Windows-only cases are skipped there and
executed by the separate Windows CI job. The required Validate job fails if that
Windows job fails or is cancelled. Existing domain/application/SQLite tests remain.

Next is [#70](https://github.com/mani8785/HomeVault/issues/70): secure operator
provisioning, invitation-only accounts and cookie authentication. No real account
enrollment or browser login is delivered by this storage-only step.
