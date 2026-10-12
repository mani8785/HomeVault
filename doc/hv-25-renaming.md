# HV-25.1: Vault and Asset renaming

Issue [#116](https://github.com/mani8785/HomeVault/issues/116) implements accepted
[ADR-0032](ADRs/0032-vault-asset-renaming.md). This is the first lifecycle slice;
metadata editing, deletion, trash and restoration remain separate work.

## Behavior

In My library, select a Vault and open **Vault information & access** to rename
it as Owner or Administrator. Select an Asset and use **Info → Rename Asset**
as Owner, Administrator or Editor. Archived Vaults are read-only. The library
refreshes after successful renaming; reopen the record to inspect its new name.
The standalone bookmarked Asset page remains a read/inspect entry point; use the
library for role-aware renaming.

Names preserve exact whitespace, case and Unicode. Blank names are rejected;
there is no new length, normalization or uniqueness rule. Names are ordinary
metadata visible to current members, so keep secrets in Sensitive attributes.

Every request carries the exact last-read name. A different stored name yields
`409 name_conflict`, with neither name echoed in the error. The UI retains the
draft and blocks further submission until fresh metadata is loaded. Note the
draft before refreshing the library, reopen the record, review its current name
and deliberately retry. There is no automatic overwrite or retry. Comparing names
cannot detect an intervening change that returns to the original name (ABA).

## HTTP and storage

`PUT /api/v1/vaults/{vaultId}/name` and
`PUT /api/v1/assets/{assetId}/name` accept only `name` and `expectedName`.
Authentication and antiforgery are required. Successful requests return 204,
including an unchanged name with a matching expectation. Missing/inaccessible
records return identical 404; insufficient role returns 403; archived writes
return `409 vault_archived`. Invalid name/expected name returns 400 with a safe
field code. See the [OpenAPI contract](openapi/homevault-v1.json).

Current access and lifecycle are checked before name validation and comparison
inside a non-deferred SQLite write transaction. An Asset's Vault is resolved
from storage. Domain behavior validates the replacement; storage updates only
the name column. No migration, operator key access or encrypted host is needed.
Guid identities, Vault type/memberships, attributes, Evidence, incoming/outgoing
Relationships, Reminders and encrypted value bindings are preserved.

## Owner validation

Stop the running API before Release builds on Windows. From the repository root:

```powershell
git switch codex/hv-25-rename
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
cd frontend/homevault
pnpm install --frozen-lockfile --ignore-scripts
node node_modules/@angular/cli/bin/ng.js build --configuration production
node node_modules/@angular/cli/bin/ng.js test --watch=false
```

Use the existing [HTTPS startup guide](hv-23-angular-ui.md) and a disposable
database with real accounts. No migration is added in this slice.

1. Rename a Vault and Asset, including whitespace and non-ASCII characters.
   Refresh/restart and confirm exact names and stable record links.
2. Confirm Owner/Administrator can rename both; Editor can rename only Assets;
   Viewer and archived Vaults have no rename form. Server rejects stale role
   hints and archived writes.
3. Open the same record in two browsers, rename in one, then submit the old form
   in the other. Confirm conflict, retained draft and blocked retry; refresh,
   reopen and deliberately submit against the new name.
4. Confirm blank input does not submit and keyboard/narrow-screen forms work.
5. With related data and an encrypted host, confirm ordinary attributes, Note/URL
   Evidence, both Relationship directions, Reminders and deliberately revealed
   Sensitive values still work after rename. Sensitive values never auto-reveal.

Added tests cover Domain invariants, trusted-actor/cancellation handling, SQLite
atomicity/concurrency/rollback/related-row preservation, unchanged ciphertext with
successful decryption, HTTP access/antiforgery and UI conflict/selection behavior.
Tests and PR/CI checks left to the user; not run by the agent.
