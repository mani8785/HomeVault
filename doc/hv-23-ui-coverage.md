# HV-23 UI expansion: personal library workspace

The owner requested a Zotero-inspired layout on 2026-10-10: a compact Vault tree
on the left, selectable Assets in the center, and an inspector on the right.
This is HomeVault's vocabulary and behavior, not a bibliographic application clone.
Retain independent Angular/API projects and the accepted security boundaries.

## Delivery slices

| Slice | UI result | Backend state |
| --- | --- | --- |
| Library queries and shell (#96) | Browse/search owned or shared Vaults and Assets; page results; create records and select details | [ADR-0031](ADRs/0031-library-browsing-queries.md) accepted 2026-10-10 |
| Account access | Sign-in/out, invitation redemption, recovery redemption and sign out all sessions | Existing HTTP operations |
| Attributes and Evidence | List/add/change/remove ordinary attributes; list/add/read/remove URL or Note Evidence | Existing HTTP operations; no file upload implemented |
| Relationships and Reminders | Create/read/remove Covers links; create/read/update/complete/cancel Reminders; deliberate action reveal | Existing by-id operations; collection queries in ADR-0031 |
| Vault administration | Archive confirmation, add member, change role, remove member; preserve last Owner | Existing writes; member list in ADR-0031 |
| Sensitive values | Metadata list, add/change/remove, explicit reveal/hide for permitted users | Existing encrypted-host endpoints only; never auto-reveal |

Database migrations, private-directory provisioning, account invitation issuance,
account disablement, key initialization/unlock/rotation, backup and recovery remain
trusted terminal operator operations. Exposing those through HTTP would change
accepted security decisions and requires a separate approved design. The UI must
explain availability and link operator guidance rather than pretend those actions
are implemented in the browser. Domain-only/in-memory demonstrations are not
additional product endpoints. File/PDF previews, tags, collections, trash and sync
from the reference image are not implemented HomeVault capabilities.

## Usability and acceptance

Tracked under [#95](https://github.com/mani8785/HomeVault/issues/95), with tasks
#96 (library), #97 (accounts), #98 (attributes/Evidence), #99 (links/Reminders),
#100 (Vault administration), and #101 (Sensitive controls).

Use dense readable rows, restrained neutral colors, a clear selected item, toolbar
actions, an inspector with labelled sections, visible keyboard focus and a narrow
screen layout that shows one pane at a time. No fake records or inactive toolbar
buttons. Use server results for lists; temporary form state is not a database.

Each slice gets a focused issue/PR and manual acceptance steps. PR #94 remains
the initial three-operation journey; follow-ups depend on it without merging it.
The owner runs unit/integration tests and checks PR/CI results. The agent may build
or type-check its implementation, writes meaningful tests, and reports checks not
run. This request authorizes working through these UI slices, not automatic merges.

## Owner validation

Stop a running API before building Release; Windows may lock its DLLs.
Run from the repository root, checking each command succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore -warnaserror
dotnet test -c Release --no-build --no-restore
cd frontend/homevault
pnpm install --frozen-lockfile --ignore-scripts
node node_modules/@angular/cli/bin/ng.js build --configuration production
node node_modules/@angular/cli/bin/ng.js test --watch=false
```

Use [the HTTPS setup/run guide](hv-23-angular-ui.md) for both terminals. The root
page is now the library. Create a Vault and Asset, return to My library, select
the Vault and Asset, search a visible name, and inspect the record. Confirm that
another account sees only its memberships, archived Vaults remain readable, and
a removed membership disappears after Refresh. Check keyboard and narrow panes.
PR/CI checks and test execution are left to the owner, not verified by the agent.
