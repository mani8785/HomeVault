# HV-22.3: Authenticated Vault and Asset API

Subsequent [HV-22.4.1](hv-22-vault-archive.md) adds Owner-only Vault archival to
the versioned contract. The three-operation description below records HV-22.3;
membership changes and other record operations remain later work.

Implements [#71](https://github.com/mani8785/HomeVault/issues/71), following accepted
[ADR-0019](ADRs/0019-browser-ui-first-journey.md) and
[ADR-0020](ADRs/0020-local-accounts-vault-authorization.md).

The existing cookie host now exposes CreateVault, RegisterAsset and InspectAsset.
HttpCurrentActor supplies the Guid from the validated HomeVault cookie identity.
No body, query, header or fictional default supplies identity. Each request first
validates account enabled state/security stamp, then the existing application use
case and SQLite adapter enforce current membership and lifecycle rules.

## Contract

The reviewed [OpenAPI 3.0 contract](openapi/homevault-v1.json) is embedded in the API
assembly and served at authenticated GET /api/v1/openapi.json. This is a versioned
document, not an inferred serialization of Domain/EF classes. Contract tests compare
actual status codes, media types and response shapes with it. No new dependency is
needed to serve it, and no database migration is introduced by this slice.

| Route | Request | Success |
| --- | --- | --- |
| POST /api/v1/vaults | name, type | 201: id, name, type, status |
| POST /api/v1/vaults/{vaultId}/assets | name | 201: id, vaultId, name; Location points to Asset GET |
| GET /api/v1/assets/{assetId} | Guid route reference | 200: id, vaultId, name |

Types are the case-sensitive strings personal, household, organization. Created
status is active. Names must be nonblank and are preserved exactly. Null/missing
names or types are rejected. IDs are generated on the server and returned in
hyphenated Guid D format. Routes accept either hex case but reject empty/malformed
IDs with 400. Clients must not infer access from possessing an ID.

Unknown JSON fields are rejected, including actorId, ownerId, id, attributes and
Sensitive values. Identity-looking query parameters/headers are ignored. Explicit
transport DTOs expose only the fields above. Vault creation does not return a
Location header because no Vault-read endpoint exists. Do not automatically retry
POST: repeated requests create separate records, and a lost response does not
prove the database transaction failed.

Responses use no-store. The /api/v1 boundary returns safe Problem Details for
failures, with type=about:blank, a constant title, status, code and an errors object.
Field errors contain only stable codes (for example errors.name=[blank_name]);
responses never echo input values, SQL errors, paths or exception text.

| Status | Meaning / example code |
| --- | --- |
| 400 | invalid_request, blank_name, invalid_type, invalid_identity; includes antiforgery failures |
| 401 | unauthenticated |
| 403 | forbidden: current member without write permission |
| 404 | unavailable: missing and inaccessible share the exact same payload |
| 409 | vault_archived or identity_conflict |
| 413 / 415 / 429 | request_too_large / unsupported_media_type / rate_limited |
| 500 | request_failed: unexpected failure, including unavailable storage at this API boundary |

The existing /auth boundary retains its earlier contract, including safe 503 for
unexpected identity-storage failures. Neither boundary accepts stale authentication
when storage fails. Unexpected failures do not prove that no write committed.

## Permissions and transactions

An authenticated account creates a Vault as its sole initial Owner. Owner,
Administrator and Editor may register Assets in an Active Vault. Viewer receives
403. Authorized writers receive 409 when the Vault is Archived; Viewer still gets
403 because role checks precede lifecycle checks. Nonmembers always receive the
same 404 as a missing Vault. All current members may inspect an existing Asset,
including in an Archived Vault.

SQLite registration retains its immediate write transaction: current role/archive
checks and insertion serialize with competing mutations. Reads use a single
membership-scoped query, without an unscoped fallback or permission cache. Removing
membership or changing roles affects the next request without logging in again.
The tests also pause a request after authentication and commit archive/removal
before registration, proving the write checks cannot rely on earlier identity checks.
Existing adapter tests exercise competing SQLite transactions and access-before-
conflict ordering. In-flight work that already committed cannot be revoked.

No membership/archive HTTP mutation is exposed yet; tests arrange those states
directly in isolated fixture databases. Real membership management, additional
operations and Sensitive access remain #72. This step is the review checkpoint
for considering the first Angular journey; it does not close parent #26 or start
the parked UI automatically.

## Terminal validation

Run each command from a clean checkout and stop on failure:

```powershell
cd D:\repos\HomeVault
git switch codex/hv-22-3-authorized-api
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/authorized-api
dotnet ef migrations has-pending-model-changes --project src/HomeVault.Infrastructure --configuration Release --no-build
```

The API tests use two real Identity accounts, HTTP cookies and SQLite. They cover
all roles in Active/Archived states, anonymous/revoked identities, antiforgery,
forged fields, identical missing/inaccessible responses, current permission
changes, host restart, safe storage errors and OpenAPI response conformance.
There is no test identity switch in the normal executable.

## Run and exercise the API

First follow [account setup](hv-22-accounts.md) for private directories, explicit
migration, session keys, invitation redemption and the local HTTPS certificate.
Use the same Windows account and existing configured database/key paths:

```powershell
$accountHome = "$env:LOCALAPPDATA/HomeVault-Accounts"
$accountDatabase = "$accountHome/accounts.db"
$accountKeys = "$accountHome/session-keys"
dotnet run --project src/HomeVault.Api -c Release --no-build -- serve $accountDatabase $accountKeys
```

The host listens at https://localhost:7443; stop with Ctrl+C. There is no Angular
page yet. All three routes require login, and both POSTs require fresh antiforgery
state. The following direct PowerShell commands can exercise an **already enrolled**
fictional account in a second terminal. Get-Credential prompts rather than placing
a literal password in history. PowerShell sends its transient value in an HTTPS
body, not external-process arguments. Do not print credential/body/cookie variables
or enable request/transcript diagnostics while using private data.

```powershell
$origin = 'https://localhost:7443'
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
Invoke-WebRequest "$origin/auth/antiforgery" -WebSession $session | Out-Null
$credential = Get-Credential -Message 'HomeVault account'
$csrf = $session.Cookies.GetCookies($origin)['XSRF-TOKEN'].Value
$loginBody = @{ login = $credential.UserName; password = $credential.GetNetworkCredential().Password } | ConvertTo-Json
Invoke-RestMethod "$origin/auth/login" -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body $loginBody
Remove-Variable loginBody, credential
Invoke-WebRequest "$origin/auth/antiforgery" -WebSession $session | Out-Null
$csrf = $session.Cookies.GetCookies($origin)['XSRF-TOKEN'].Value
$vault = Invoke-RestMethod "$origin/api/v1/vaults" -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body '{"name":"Fictional household","type":"household"}'
$asset = Invoke-RestMethod "$origin/api/v1/vaults/$($vault.id)/assets" -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body '{"name":"Fictional bicycle"}'
Invoke-RestMethod "$origin/api/v1/assets/$($asset.id)" -WebSession $session
```

The last command deliberately displays fictional Asset metadata. A second account
without membership receives 404 for that Asset. After a host restart, an unexpired
cookie and unchanged persisted session keys allow the same GET to return the
stored metadata. These POSTs are not an idempotent demo: repeating them creates
new records. No scripts, frontend build or new migration are required for this step.
