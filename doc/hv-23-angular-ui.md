# HV-23: Independent Angular first journey

Issue [#27](https://github.com/mani8785/HomeVault/issues/27) implements accepted
[ADR-0019](ADRs/0019-browser-ui-first-journey.md). The owner resumed this work on
2026-10-09 after authentication and encryption implementation.

## What a user can do

Sign in with an existing invitation-only HomeVault account, create a personal,
household or organization Vault, add an Asset, and bookmark its detail URL.
Opening or refreshing that URL fetches current data from SQLite through the API.
Sign out removes the session. Names render as text, including HTML-looking names.

The frontend is `frontend/homevault`. Its forms and small HTTP service contain no
database models or business authorization rules. The existing authenticated API
creates identifiers and checks current membership, role and archival state.
Angular route guards improve navigation; they are not an authorization boundary.
There are no backend or schema changes in this slice.

Every mutation first refreshes the antiforgery cookie, then sends Angular's
`X-XSRF-TOKEN` header. This also handles the change in identity after login.
Requests are relative to the UI origin; the local proxy forwards `/auth/**` and
`/api/**` to the API with certificate verification enabled. No CORS exception is
needed. UI route fallback never handles those API paths.

Forms prevent repeated in-flight submission and preserve ordinary input after
failure; passwords are cleared after sign-in completes. POSTs are never retried
automatically. This is not an exactly-once guarantee: if a response is lost, the
server may have committed the operation. Leaving or changing a route cancels
late UI callbacks, not a transaction already received by the server.

Safe messages cover authentication, authorization, unavailable records, archived
Vaults, invalid input, rate limits and service failures. Server exception text is
never rendered. Record payloads remain in component memory only, without browser
storage or service-worker caching. The Vault confirmation is transient; reopening
it offers the next action without inventing a Vault-read API.

## Validate from PowerShell

From a clean checkout of this PR branch (do not discard local work):

```powershell
cd D:\repos\HomeVault
git fetch origin
git switch codex/hv-23-angular-first-journey
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/hv23
```

Use Node **24.19.0**, pinned in `.node-version`, and pnpm **11.25.0**. Install Node
from its official distribution if necessary. With Node/npm on PATH:

```powershell
node --version
npm install --global pnpm@11.25.0 --ignore-scripts
cd frontend\homevault
pnpm install --frozen-lockfile --ignore-scripts
node node_modules/@angular/cli/bin/ng.js build --configuration production
node node_modules/@angular/cli/bin/ng.js test --watch=false
```

Run each command only after the preceding command succeeds. Expect a production
bundle under `dist/homevault/browser` and 40 passing frontend tests. NUnit counts
vary by OS because Windows custody tests cannot run on Linux. The existing CI
requires Windows and Linux backend checks, then the locked frontend install,
production build and non-interactive tests. No package-command wrappers or helper
scripts are added.

Angular/CLI **22.2.2**, TypeScript **6.0.3**, RxJS **7.8.2**, Vitest **5.0.3** and
jsdom **30.1.2** are pinned. See the official
[Angular compatibility table](https://angular.dev/reference/versions) and
[Angular testing guide](https://angular.dev/guide/testing). `pnpm-lock.yaml` pins
transitive packages. pnpm's workspace file records only version-specific release-age
exceptions for the explicitly pinned Angular packages; it does not disable the
policy globally. Dependency lifecycle scripts are disabled during installation.

If using Codex's bundled Node without npm on PATH, the already installed package
manager can be invoked directly instead of the `pnpm` executable:

```powershell
node "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\node_modules\pnpm\bin\pnpm.cjs" install --frozen-lockfile --ignore-scripts
```

That path is an environment convenience, not a repository dependency.

## Run locally in two terminals

The API and its Windows-protected session keys require Windows. First complete
the [account setup and invitation procedure](hv-22-accounts.md), using a dedicated
database outside the checkout. Invitation redemption remains the existing HTTP
operator/client workflow; this slice adds sign-in, not an enrollment or recovery
screen. Use an already enrolled account. Never paste real passwords or invitation
secrets into shell arguments, task logs or source files.

After the Release build above, terminal 1 at the repository root:

```powershell
$accountHome = "$env:LOCALAPPDATA/HomeVault-Accounts"
$accountDatabase = "$accountHome/accounts.db"
$accountKeys = "$accountHome/session-keys"
dotnet run --project src/HomeVault.Api -c Release --no-build -- serve $accountDatabase $accountKeys
```

Expect `https://localhost:7443`. Migrations and session-key creation are explicit
setup operations, never performed by opening the UI. The ordinary host supports
the entire UI journey; an encrypted host can also serve it after its documented
unlock procedure. No Sensitive attributes are displayed by this UI.

For local HTTPS, export the ASP.NET development certificate into a **new private
directory** outside the repository. Run the first three commands once from the
repository root; approve the operating system's trust prompt yourself if shown:

```powershell
$uiTls = "$env:LOCALAPPDATA/HomeVault-UI-TLS"
dotnet run --project src/HomeVault.Api -c Release --no-build -- private-directory $uiTls
dotnet dev-certs https --trust
dotnet dev-certs https --export-path "$uiTls/localhost.pem" --format Pem --no-password
```

The export includes `localhost.key`, an unencrypted private development key
protected by that directory's ACL. Do not commit, share or synchronize it.
Retain it only for local development. Renew the export after certificate renewal.
Do not use these development certificates for public hosting.

Terminal 2, after the frontend install:

```powershell
cd D:\repos\HomeVault\frontend\homevault
$uiTls = "$env:LOCALAPPDATA/HomeVault-UI-TLS"
$env:NODE_EXTRA_CA_CERTS = "$uiTls/localhost.pem"
node node_modules/@angular/cli/bin/ng.js serve --ssl-cert "$uiTls/localhost.pem" --ssl-key "$uiTls/localhost.key"
```

Open **https://localhost:4200**. Both servers bind to loopback. The extra CA file
lets Node verify the backend's local certificate; do not replace it with
`secure: false` or globally disable TLS verification. If the browser shows a
certificate warning, stop and correct local trust instead of bypassing it.
Stop each server with **Ctrl+C**. The built frontend is not a hosting deployment;
production reverse-proxy, TLS and deep-link hosting configuration remain HV-24.

## Verification and current limits

Frontend tests exercise DTOs, antiforgery order, safe failures, account changes,
duplicate clicks, retained input, password clearing, safe return URLs, late route
responses, focus links and HTML output encoding. Existing NUnit API integration
tests cover durable create/read, missing/inaccessible equivalence, unauthenticated
requests, Viewer/archival rejection and current membership checks.

Manual browser checks use a disposable real-account SQLite database: sign-in,
blank-name errors, keyboard submission, Vault confirmation, Asset detail and
HTML-looking names. Reopening the saved Asset after stopping and restarting the
API checks persistence independently of frontend memory. Narrow-screen verification
uses a 390px viewport. These checks support the first journey, not an accessibility
certification or a cross-browser compatibility claim.

The UI intentionally has no list/search, edit/delete, membership administration,
attributes, Evidence, Relationships or Reminders screens. Keep Asset bookmarks;
a later browsable dashboard needs actor-filtered list queries and pagination.
Names are ordinary metadata; do not put passwords or other secrets in them.
