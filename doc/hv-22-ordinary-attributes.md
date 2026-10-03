# HV-22.4.3: Durable ordinary Asset attributes

Implements [#78](https://github.com/mani8785/HomeVault/issues/78) under
[ADR-0023](ADRs/0023-durable-ordinary-attributes.md), accepted 2026-10-03.
Parent #72/#26 remain open; encryption and the later operation slices remain separate.

## Behavior and storage

Members can list an Asset's ordinary text attributes, including in Archived
Vaults. Owner/Administrator/Editor can add, change and remove attributes in Active
Vaults. Add requires explicit `ordinary` classification. No operation stores,
reads, overwrites or downgrades Sensitive values.

Names are trimmed and matched with .NET ordinal-ignore-case equality; original
spelling survives changes. Values retain whitespace. Add rejects duplicates,
change never upserts or renames, and removing a missing attribute fails. Existing
snapshots remain unchanged. Values are ordinary plaintext, including in backups.

Application obtains the trusted session actor once and delegates to a narrow
atomic store. SQLite write transactions serialize current membership, role and
archive checks with mutation. Read transactions keep membership and attribute
projection consistent. Classification metadata is checked before loading values;
unsupported stored rows cause a safe failure. Domain restoration rejects invalid
entries and logical duplicates before mutation. Only the affected attribute row
is written. No generic aggregate save, new package or new script is introduced.

Migration `20261003112737_AddOrdinaryAttributes` adds `AssetAttributes`, keyed by
AssetId and binary Name, with restricted Asset FK and `Sensitivity = 0` constraint.
The Domain comparer enforces logical uniqueness under the writer transaction;
raw external SQL is not a supported writer. Existing accounts, credentials,
Assets and memberships are preserved. EF mapping remains separate from DbContext.

## Validate from PowerShell

Start with a clean working tree; run commands individually and stop on failure.

```powershell
cd D:\repos\HomeVault
git fetch origin
git switch codex/hv-22-4-3-attribute-contract
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/ordinary-attributes
dotnet ef migrations has-pending-model-changes --project src/HomeVault.Infrastructure --configuration Release --no-build
```

Tests cover Domain snapshots and invalid restoration, trusted actor handling,
Unicode duplicates, all roles, cross-Vault isolation, archive/permission races,
corrupt classifications, rollback, previous-schema upgrade and verified backup/
restore. HTTP tests use real accounts, cookies, SQLite and restart reads, and
check responses against [OpenAPI 1.3.0](openapi/homevault-v1.json).

Local validation on 2026-10-03: restore and formatting verification passed;
Release build completed with zero warnings/errors; 506 tests passed (238
Domain/Application, 199 Infrastructure, 69 API), none skipped. EF reported no
pending model changes. Current-revision Windows/Linux CI remains a delivery gate.

## Upgrade and run locally

First complete [account setup](hv-22-accounts.md), including private directories,
session keys and the HTTPS certificate. These commands assume the existing local
account database. Stop the API and other writers before backup/migration; the API
never migrates automatically. The backup path must be new. Build successfully first.

```powershell
$accountHome = "$env:LOCALAPPDATA/HomeVault-Accounts"
$accountDatabase = "$accountHome/accounts.db"
$accountKeys = "$accountHome/session-keys"
$backupPath = "$accountHome/before-attributes-$([Guid]::NewGuid().ToString('N')).db"
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage backup $accountDatabase $backupPath
dotnet run --project src/HomeVault.Playground -c Release --no-build -- storage migrate $accountDatabase
dotnet run --project src/HomeVault.Api -c Release --no-build -- serve $accountDatabase $accountKeys
```

The API listens at `https://localhost:7443`; stop with Ctrl+C. For recovery, follow
the [verified restore procedure](hv-20-persistence.md) and the
[account restore precautions](hv-22-accounts.md); retain the known-good original
and backup. There is no Angular page in this slice.

In a second terminal, follow the [authenticated journey](hv-22-authorized-api.md)
to obtain `$origin`, `$session`, refreshed `$csrf` and a fictional `$asset`.
Then exercise the new operations with fictional ordinary data:

```powershell
$attributes = "$origin/api/v1/assets/$($asset.id)/attributes"
Invoke-WebRequest "$attributes/add" -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body '{"name":"Material","value":"Steel","sensitivity":"ordinary"}'
Invoke-RestMethod $attributes -WebSession $session
Invoke-WebRequest "$attributes/change" -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body '{"name":" material ","value":"Aluminium"}'
Invoke-RestMethod $attributes -WebSession $session
Invoke-WebRequest "$attributes/remove" -Method Post -WebSession $session -ContentType 'application/json' -Headers @{'X-XSRF-TOKEN'=$csrf} -Body '{"name":"MATERIAL"}'
Invoke-RestMethod $attributes -WebSession $session
```

Writes return 204 with no body; reads show Steel, then Aluminium, then an empty
array (assuming no other attributes). Duplicate add returns 409, missing change/
remove returns 404, Viewer writes return 403, and allowed-role archived writes
return 409. Sensitive or missing classification returns 400. Missing Assets and
nonmember access share the same safe 404. Do not display real private values or
enable request-body diagnostics when adapting the example.
