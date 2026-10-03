# HV-22.4.2: Authorized durable Vault membership

Implements [#77](https://github.com/mani8785/HomeVault/issues/77) under accepted
[ADR-0022](ADRs/0022-remaining-authorized-operations.md). Parent #72 and story #26
remain open. No schema migration, package, account-directory API or script is added.

## HTTP contract

All routes require the validated HomeVault cookie and antiforgery header. The
requesting actor comes only from ICurrentActor. targetActorId is the account whose
membership is being managed, never an override for the requester. Identifiers use
non-empty Guid D format; roles are lowercase owner, administrator, editor, viewer.
Unknown JSON fields are rejected. DELETE accepts no body. Responses use no-store.

| Method and route | Body | Success |
| --- | --- | --- |
| POST /api/v1/vaults/{vaultId}/members | targetActorId, role | 204, empty |
| PUT /api/v1/vaults/{vaultId}/members/{targetActorId}/role | role | 204, empty, including an authorized unchanged role |
| DELETE /api/v1/vaults/{vaultId}/members/{targetActorId} | None | 204, empty |

Owners may manage all roles while preserving at least one Owner. Administrators
may add Editors/Viewers and change/remove only Editors/Viewers. Both the old and
new roles must be permitted: an Administrator cannot demote another Administrator
or Owner, or promote themselves. Adding an already privileged target as an
ordinary member also returns 403 to an Administrator. Editors/Viewers cannot
manage membership. Self-removal/demotion follows the same rules as other targets.

Every write, including an unchanged role, requires an Active Vault. No operation
deletes an account, Asset or Vault. Changing/removing a membership whose account
is disabled remains possible; adding one requires an existing enabled account.
Account lockout is not disablement. No account is created, email sent, or invitation
issued by these endpoints. The target obtains their Guid from their own existing
/auth/session response and shares it out of band with the authorized manager.

## Validation and precedence

Authentication/antiforgery and transport checks precede storage. Application reads
the trusted actor once and validates non-empty identities and defined roles.
Inside one non-deferred SQLite write transaction, the adapter checks:

1. Current requesting membership: missing/nonmember returns unavailable.
2. Requester role, requested role and (where present) current target role.
3. Validated stored Vault state and Active lifecycle.
4. Existing enabled target account for addition only.
5. Domain mutation rules: duplicate, missing target, or last-Owner conflict.
6. Persist only the affected membership row and commit.

The order avoids disclosing target-account details to unauthorized requesters.
Vault.Restore validates the snapshot and the existing Domain methods enforce
membership invariants. No generic load/save API, public setters or EF types enter
Domain/Application. Fictional Playground actor contracts remain unchanged; the
new addition boundary requires real accounts without retrofitting an Identity
foreign key onto historical membership rows.

| Status | Safe code / meaning |
| --- | --- |
| 400 | invalid_request, invalid_identity or invalid_role; includes antiforgery failures |
| 401 | unauthenticated |
| 403 | forbidden role/transition |
| 404 | unavailable: missing/nonmember Vault, missing target membership, or missing/disabled addition target |
| 409 | vault_archived, duplicate_member or last_owner |
| 413 / 415 / 429 | Existing request size, JSON content type (POST/PUT), and rate limits |
| 500 | request_failed; no SQL, account metadata, paths or exception text |

Repeating addition returns a duplicate conflict; repeating removal returns
unavailable. Retrying after a lost response requires checking the intended state;
this slice provides no membership-list endpoint or automatic business retries.
The OpenAPI document is version 1.2.0 within the existing /api/v1 boundary.

## Concurrency and tests

Each operation obtains the SQLite write reservation before reading permission,
target state or the Owner count. Two Owners concurrently removing/demoting each
other cannot both succeed. Concurrent duplicate additions insert one membership.
Archive, membership writes and Asset registration share the SQLite writer boundary.
Operations committed before revocation remain committed; subsequent reads/writes
query current membership without requiring login again.

Tests cover the complete 64-case role-transition matrix, add/remove matrices,
disabled targets, last-Owner races, duplicate races, archive/permission/account
changes through independent connections, rollback and cancellation. HTTP tests use
real Identity accounts, cookies and SQLite, validate OpenAPI response shapes,
and pause operations after authentication to prove committed permission/archive
changes prevent the write. Host restart tests reuse fixture keys; they are not a
browser/OS restart test. Domain rules and schema are reused, not weakened.

## Terminal validation and fictional journey

Run from the repository root, stopping if any command fails:

```powershell
git fetch origin
git switch codex/hv-22-4-2-vault-membership
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/vault-membership
dotnet ef migrations has-pending-model-changes --project src/HomeVault.Infrastructure --configuration Release --no-build
```

First complete [account setup](hv-22-accounts.md) and the
[authenticated API journey](hv-22-authorized-api.md) using fictional accounts and
an Active test Vault. Start the existing API with configured private paths:

```powershell
dotnet run --project src/HomeVault.Api -c Release --no-build -- serve $accountDatabase $accountKeys
```

Use a second terminal with the Owner session $session, fresh $csrf, $origin and
$vault from the API guide. Obtain the second enrolled account Guid from that
account's /auth/session response; no password or cookie is shared with the Owner.

```powershell
$targetActorId = [Guid](Read-Host 'Second fictional account Guid')
$memberUrl = "$origin/api/v1/vaults/$($vault.id)/members"
$body = @{ targetActorId = $targetActorId.ToString('D'); role = 'editor' } | ConvertTo-Json
Invoke-WebRequest $memberUrl -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body $body
Invoke-WebRequest "$memberUrl/$($targetActorId.ToString('D'))/role" -Method Put -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body '{"role":"viewer"}'
Invoke-WebRequest "$memberUrl/$($targetActorId.ToString('D'))" -Method Delete -WebSession $session -Headers @{'X-XSRF-TOKEN'=$csrf}
```

Expect 204 for each successful mutation. To observe authorization, pause after
each command and use the second account's independent authenticated session:
Editor can register an Asset, Viewer can read but receives 403 on registration,
and a removed member receives 404 on existing Asset reads. Do not print or share
cookies, credentials or private content. Stop the host with Ctrl+C. UI, ordinary
attribute persistence and other remaining tasks are outside this slice.
