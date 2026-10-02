# HV-22.2: Invitation-only accounts and cookie authentication

Implements [#70](https://github.com/mani8785/HomeVault/issues/70) under accepted
[ADR-0020](ADRs/0020-local-accounts-vault-authorization.md) and
[ADR-0021](ADRs/0021-operator-invitations-recovery.md).

HomeVault.Api is a Windows-only local HTTPS host and an offline operator entry
point. It authenticates accounts, but does not yet expose Vault/Asset operations
(#71) or an Angular interface. There is no public signup, email delivery, default
password, first-user-wins setup or automatic migration. Use a fresh account database;
existing fictional memberships are never reassigned.

## Storage and authentication

The explicit AddAccountCredentials migration adds hashed invitation/recovery
credentials, normalized login, purpose, expiry and consumption state. It leaves
the existing tables intact. All account mutations take a SQLite immediate write
transaction, including Identity password updates, lockout and credential consumption.
Invalid password/credential attempts roll back redemption; login failures commit
their lockout count. One concurrent redemption succeeds, with later contenders
seeing consumption. Do not log EF sensitive data or serialize Identity entities.

Invitations use 256-bit random secrets and expire after 24 hours. Reissue revokes
old credentials for that login/purpose. Recovery uses Identity Data Protection
reset tokens and a hash-only one-use record, both with a 30-minute lifetime.
Issuing recovery rotates the stamp immediately; reset rotates it again and clears
lockout. Recovery cannot enable disabled accounts. Enrollment/reset do not sign in
automatically and enrollment grants no existing Vault memberships.

Identity V3 hashes passwords with its maintained PBKDF2 implementation, configured
at 210,000 iterations. Passwords must contain 15–128 UTF-16 code units; spaces and
password-manager paste are supported and character-class composition is not required.
Five failed checks lock an account for 15 minutes. Unknown, disabled, locked and
incorrect accounts all return 401 with no explanatory body. Rejected account states
perform password-hash work; this does not guarantee timing indistinguishability.

The authentication cookie is __Host-HomeVault: host-only, Path=/, Secure, HttpOnly,
SameSite=Lax, nonpersistent and eight-hour absolute lifetime with no sliding renewal.
Every authenticated request rechecks the account's enabled state and security stamp
in storage. Unavailable storage returns a safe 503 without accepting stale identity.
Logout deletes the browser cookie; sign-out-all rotates the stamp. Already running
requests may finish. No roles are taken from the cookie.

## HTTP contract

The normal executable binds only https://localhost:7443, requires the localhost
Host header and configures no credentialed CORS. Development certificates are for
local development only; public hosting requires the separate deployment review.
All request bodies are capped at 16 KiB, including chunked bodies. Credential
endpoints share a 10-request/minute limiter; all routes share a 60-request/minute
limiter with no queue. These local, process-wide limits reset on restart.

| Method and route | Access | Outcome |
| --- | --- | --- |
| GET /auth/antiforgery | Anonymous | 204; secure readable XSRF-TOKEN cookie plus HttpOnly antiforgery cookie |
| POST /auth/login | Anonymous + antiforgery | Login/Password JSON; 204 or generic 401 |
| POST /auth/invitations/redeem | Anonymous + antiforgery | Login/Secret/Password JSON; 204 or generic 400 |
| POST /auth/recovery/redeem | Anonymous + antiforgery | Login/Secret/Password JSON; 204 or generic 400 |
| GET /auth/session | Authenticated | 200 with server-issued actorId |
| POST /auth/logout | Authenticated + antiforgery | 204; clears this browser's cookies |
| POST /auth/sign-out-all | Authenticated + antiforgery | 204; revokes sessions and outstanding account credentials |

Read XSRF-TOKEN and send its value in X-XSRF-TOKEN for every POST, also sending the
cookie jar. Fetch fresh antiforgery state after login, logout or reset. Anonymous
tokens cannot authorize unsafe operations under the newly authenticated identity.
Token values and passwords belong only in HTTPS bodies, never URL query strings.
401/403 responses are not redirects. Unknown routes inherit authentication by default.
Responses are not cached. Normal host logging providers are disabled to avoid
request or identity diagnostics leaking private inputs; operators receive constant
success/failure messages. Future logging needs an explicit redaction review.

## Build and validate from PowerShell

Run commands separately and stop if any fails:

```powershell
cd D:\repos\HomeVault
git switch codex/hv-22-2-operator-workflow
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/accounts
dotnet ef migrations has-pending-model-changes --project src/HomeVault.Infrastructure --configuration Release --no-build
```

## First local setup

Run as the Windows account that will run HomeVault, from an ordinary non-elevated
interactive terminal. Use fresh paths outside source control and synchronized folders.
The parent LOCALAPPDATA directory already exists. The following explicit command
creates a new directory with private Windows permissions; it refuses an existing one.

```powershell
$accountHome = "$env:LOCALAPPDATA/HomeVault-Accounts"
dotnet run --project src/HomeVault.Api -c Release --no-build -- private-directory $accountHome
$accountDatabase = "$accountHome/accounts.db"
$accountKeys = "$accountHome/session-keys"
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage migrate $accountDatabase
dotnet run --project src/HomeVault.Playground -c Release --no-build -- session-keys initialize $accountKeys
dotnet dev-certs https --trust
```

For existing databases, stop all writers and follow the
[backup-before-upgrade procedure](hv-20-persistence.md) before explicit migration.
Startup refuses broad ACLs, foreign owners, reparse paths, absent keys and pending
migrations. Do not bypass these checks by opening access to other users. The API
and operator modes hold the same exclusive persistent .host-lock file beside the
database. The handle is released at exit; the file stays and must not be deleted
to bypass exclusion. This is a cooperative single-host guard, not a defense from
the trusted Windows user modifying files or running unrelated SQLite tools.

## Invite and run

With the server stopped, issue an invitation to the first recipient:

```powershell
dotnet run --project src/HomeVault.Api -c Release --no-build -- invite $accountDatabase $accountKeys "$accountHome/invitation.json"
```

The command prompts for the login with input hidden. It rejects redirected
input/output. No login, password or secret goes in arguments or environment
variables. The file is created with private ACLs before any secret is written;
existing files are never overwritten. The JSON contains Version, Purpose, Login,
Secret and Expires. Transfer it only through a trusted encrypted channel after
verifying the recipient, then remove your exported copy. The file itself is
plaintext under Windows ACLs, not encrypted. Deletion is not secure erasure.
Never print or paste its content into task/PR logs. Failed export may leave an
empty/partial private file and an issued credential: use a new filename and reissue,
which revokes the old credential. Do not claim failed delivery succeeded.

```powershell
dotnet run --project src/HomeVault.Api -c Release --no-build -- serve $accountDatabase $accountKeys
```

Expect the localhost URL and use Ctrl+C to stop. An ordinary browser can fetch
/auth/antiforgery; /auth/session returns 401 before login. There is no login page
yet. Real credential POSTs should use a client that keeps secrets out of history;
the automated HTTP tests exercise the complete enrollment/login workflow with
fictional fixtures without requiring users to place passwords in shell commands.

## Recovery, disable and restored databases

Stop the host before these commands. Recover/disable prompt for the verified
recipient's login; recovery exports a new 30-minute file and signs out existing
sessions immediately. The recipient chooses their own password via recovery POST.

```powershell
dotnet run --project src/HomeVault.Api -c Release --no-build -- recover $accountDatabase $accountKeys "$accountHome/recovery.json"
dotnet run --project src/HomeVault.Api -c Release --no-build -- disable $accountDatabase $accountKeys
```

After restoring a verified database copy, before reopening its host, run:

```powershell
dotnet run --project src/HomeVault.Api -c Release --no-build -- invalidate-restored $accountDatabase $accountKeys
```

This atomically rotates all account stamps and consumes all outstanding invitation/
recovery records. Password hashes and Vault memberships are preserved. The operator
must perform this step: arbitrary replacement by external file tools cannot be
reliably detected by the application. Preserve the external key ring separately;
loss of that ring invalidates cookies/tokens, not Asset data. Key renewal remains
explicit under [HV-22.1](hv-22-identity-storage.md).

## Verification boundaries

Tests use real SQLite and framework TestHost HTTP cookies, including purpose-bound
antiforgery, eight-hour absolute expiry, restart, stamp invalidation, storage failure,
lockout, recovery and concurrent/replayed credentials. Windows integration checks
exercise actual private ACLs, exclusive file handles, no overwrite and DPAPI keys.
The Windows suite also reopens an actual DPAPI ring between HTTP hosts and verifies
that the cookie survives. TestHost does not test a TLS handshake; manually starting
the normal executable checks the actual local host separately. Public hosting and
Angular remain deferred.
