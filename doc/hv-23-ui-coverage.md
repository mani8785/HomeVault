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

Each slice gets a focused issue and manual acceptance steps; related inspector
slices share one PR. PR #94 remains
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

### Account acceptance (#97)

From Sign in, open Accept invitation or Use a recovery code. Use only a disposable
operator-issued code, choose matching 15–128 character passwords and verify sign-in.
A rejected code must show a safe message and clear code/password fields. In Account,
verify the member ID and confirm Sign out all sessions; another logged-in browser
must be rejected on its next request. Invitation issuance and disablement stay in
the terminal. Agent-added tests are not executed; run the commands above yourself.

### Inspector and Vault acceptance (#98–#101)

Select a Vault and an Asset. The right-hand inspector has Info, Attributes,
Evidence, Relationships, Reminders and Sensitive sections. The saved Asset URL
also provides these sections; the server remains authoritative for every action.

| Area | Owner-run checks |
| --- | --- |
| Ordinary attributes | Add, change and remove a name/value pair; duplicate names fail safely; Viewer cannot write |
| Evidence | Add a Note and URL, deliberately read content, hide it, remove after confirmation; URLs open only on user action |
| Relationships | Select another same-Vault Asset from paged results, choose Covers direction, create, open linked Asset, remove; both Assets remain |
| Reminders | Create with local due time, explicitly read/hide action, update action/date, complete or cancel with confirmation; terminal status prevents further edits |
| Vault access | Open Vault information & access; add a known enabled member ID, change role, remove; verify Administrator restrictions and last-Owner rejection |
| Archive | Confirm archive as Owner, refresh and verify read-only controls and server rejection of stale writes |
| Sensitive | Start the encrypted host using existing operator guidance; add, replace, reveal/hide and remove; ordinary host shows unavailable state without claiming encryption is active |

Sensitive values clear on hide, panel/selection changes, navigation, focus loss,
visibility changes, failures and a 30-second reveal timeout. A late response after
Hide must not reveal the value again. No value is copied automatically or persisted
in browser storage. Clearing JavaScript state is not a guarantee of secure memory
erasure. Evidence and Reminder text is still ordinary plaintext storage; their
separate reads prevent incidental display, not encryption.

Reminder inputs use the browser's local timezone and transmit an explicit UTC
instant. Updating only action text preserves the existing exact server timestamp;
changing the date uses the input's seconds precision. No scheduler is introduced.

The expanded UI has not been browser-acceptance-tested by the agent under the
owner's new preference. Production compilation and test-file type checks are
separate from executing tests. Use disposable data for acceptance, especially
archival, membership changes and credential recovery.
