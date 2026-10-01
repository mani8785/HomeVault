# ADR-0019: Browser UI and first Vault/Asset journey

Status: Proposed
Created: 2026-10-01
Issue: [HV-23 / #27](https://github.com/mani8785/HomeVault/issues/27)

## Confirmed direction

The owner selected a browser-based application and deferred HV-21 implementation.
ADR-0018 is accepted and merged; its four implementation tasks remain parked.
The owner subsequently selected Angular with an independent ASP.NET Core API
so another frontend can replace Angular without rewriting business logic.
This direction is confirmed. Detailed contracts and the prototype boundary below
remain Proposed. Hosting and authentication providers are not selected here.

## Options and recommendation

| Option | Fit for HomeVault |
| --- | --- |
| ASP.NET Core Razor Pages | Simple server-rendered forms, but does not meet the owner's preference for an independently replaceable frontend |
| Blazor Web App, Interactive Server | Useful for richer component interactions in C#; adds persistent connection/circuit lifetime and reconnect considerations that the first three forms do not require |
| Angular and ASP.NET Core API | Selected direction: separate frontend build and HTTP contracts; introduces a second toolchain and integration tests |

Use Angular for presentation and ASP.NET Core on the existing .NET 10 stack for
the API. Frontend replacement still requires rewriting UI code; stable API
contracts protect the backend from that rewrite. Use semantic HTML and responsive
CSS; do not select a component library or global state framework without a need.
Pin a supported stable Angular/CLI release and compatible Node.js, TypeScript and
RxJS versions from the official compatibility matrix during scaffolding. Commit
the package lock and document reproducible install/build/test commands.

## Independent frontend and API contract

Keep the Angular workspace under `frontend/homevault` and the API host in
`src/HomeVault.Api`, in the same repository with independent build outputs.
Angular imports no C# implementation, database model, or persistence schema.
Use HTTP/JSON through a small frontend API service and explicit transport DTOs.
Do not serialize Domain entities, EF rows, or application result wrappers directly.

Document operations using OpenAPI and test that actual status codes and payloads
match the contract. Preserve compatible DTOs; introduce a reviewed version for
breaking changes. Use a consistent `/api/v1` prefix. Initial proposed operations:

| Operation | Request | Success |
| --- | --- | --- |
| POST /api/v1/vaults | Name and explicit Vault type | 201 with created Vault metadata |
| POST /api/v1/vaults/{vaultId}/assets | Name | 201 with Asset metadata and its API Location |
| GET /api/v1/assets/{assetId} | Route ID | 200 with Asset id, VaultId and name |

Generate new IDs on the server. Specify stable string representations for enum
values and error codes, nullability, and Guid formatting in OpenAPI. Use safe
Problem Details with field errors: 400 for invalid requests, 401 for absent trusted
identity, 403 for a known forbidden operation, 404 for missing/inaccessible records,
409 for lifecycle/identity conflicts, and generic 500 for unexpected failure.
Do not include exception text or request values. Preserve the current access-before-
conflict ordering. No Location for Vault creation should imply an unimplemented
Vault-read endpoint. Client validation improves usability; backend validation and
authorization remain authoritative. Angular route guards are not access control.

Use relative `/api` calls with a development proxy to the independently running
API. Both development servers bind only to loopback. A future same-origin reverse
proxy can serve separately built UI and API artifacts; independent code does not
require cross-origin hosting. Do not add permissive CORS as a shortcut. Keep any
SPA fallback separate from `/api` so API errors never return the Angular HTML shell.
Deep-link refresh must serve the UI shell and then fetch current data through the API.

Specify a server-issued antiforgery token bootstrap and cookie/header pairing
compatible with Angular HttpClient; validate tokens on the backend for mutations.
Angular's client support alone is insufficient. Keep this transport contract
documented for replacement clients. Authentication strategy remains HV-22; do not
introduce browser-stored bearer tokens or choose a cookie login provider here.

## Dependency on authentication

HV-23 lists HV-22 as a dependency. Choosing the UI and building a fictional local
prototype can proceed first only if the owner accepts this explicit scope:

- Development-only, loopback-only prototype with a fixed server-owned fictional
  actor and a dedicated fictional SQLite database outside the repository.
- The prototype must refuse non-Development execution and non-loopback binding;
  reject unrecognized Host headers and retain antiforgery protection on writes.
- No actor ID from forms, query strings, cookies, or headers. All local users of
  this prototype act as the same fictional identity; this is not authentication.
- No public/network deployment or real user data. HV-22 must establish a trusted
  authenticated actor and validate access at every entry point before those uses.
- Preserve current role/archive checks in the existing application/store boundary.
  Local-only binding alone is not proof of security against local processes or
  malicious browser requests; verify the development safeguards explicitly.

Alternatively, complete HV-22 before any executable UI. No silent removal of its
dependency is proposed. Encryption remains deferred; no sensitive fields are added.

## First journey

1. Landing page explains the fictional local mode and offers Create Vault.
2. Enter Vault name and type. The server generates its Guid and calls
   CreateVaultUseCase; the trusted actor becomes the initial Owner.
3. Success offers Add Asset in that Vault. Enter an Asset name; the server supplies
   a new Guid and calls RegisterAssetUseCase. Treat the route Vault ID as untrusted;
   the store must recheck membership, role, and archive state at submission time.
4. Navigate in Angular to an Asset detail URL containing its ID. InspectAssetUseCase must
   reread it on every request. Refreshing or reopening that URL proves persistence.
   Missing and inaccessible Assets share the same safe unavailable response.

After a successful POST, navigate to a read-only view; route loading and browser
refresh issue GET requests only. Disable repeated submission for usability and
avoid automatic POST retries, but do not claim this alone
provides idempotency or exactly-once creation. Preserve form input and show safe
validation messages; unexpected failures reveal no stack traces or database paths.
Render names using Angular text interpolation; do not bypass sanitization or use
raw HTML for stored values. Do not log submitted values. Route identifiers are
references, not access credentials. Use no-store responses for record APIs and do
not persist record payloads in browser storage or service-worker caches.

The first slice does not provide Vault/Asset lists, search, editing, deletion,
membership management, attributes, files, or reminders. Existing contracts have no
general list query. Reopening a saved detail URL is supported; a future browsable
dashboard needs separately scoped actor-filtered queries and pagination.

## Project and storage boundaries

Add HomeVault.Api as the HTTP/composition project after acceptance. It calls
Application use cases and wires Infrastructure adapters. Domain and Application
must not reference ASP.NET Core, Angular, transport DTOs, or EF Core. API endpoints
map transport contracts to use cases; Angular components own view state and forms.
Business validation remains in existing layers. No generic CRUD API is introduced.

Use ASP.NET Core's built-in dependency injection in this host. Retain manual
composition in Playground and avoid any service locator in business code. This
extends the accepted selective-DI approach without selecting a third-party
container. Register request-scoped identity/use cases; use the existing short-lived
context-per-operation SQLite adapters, never a singleton DbContext.

Configure a dedicated absolute SQLite path outside source control. Keep migration
an explicit terminal operation using existing tooling; never migrate on a GET or
at normal startup. Do not silently select the owner's existing database or seed
duplicate records. Missing schema/configuration yields actionable safe setup
guidance without dumping paths or connection strings into browser errors.

## Accessibility and validation

Use labelled inputs, native buttons, keyboard navigation, visible focus, a linked
validation summary, field-level messages, and responsive layouts with adequate
contrast. Do not communicate state through color alone. Manually verify keyboard
completion and narrow-screen layout; automated checks are supporting evidence.

After acceptance, implement and review these slices in order:

1. API contracts, host and development-only composition; test environment/binding/
   Host guards, configuration failures, DI boundaries, antiforgery rejection and
   OpenAPI conformance. Update architecture tests without weakening Domain isolation.
2. Independent Angular workspace, routing, API service and the three-operation UI;
   reuse SQLite adapters through the API. Add component/form and HTTP-client tests,
   plus NUnit API integration tests for success, validation, status codes, reload,
   unauthenticated use-case failures, inaccessible/missing equivalence, Viewer
   rejection, archived writes, and current membership checks. Test actors belong
   in test composition, never an exposed identity-switching endpoint.
3. Browser verification and documentation; test actual form navigation, reload
   from SQLite after a host restart, output encoding, safe errors, keyboard flow
   and responsive layout. Review any new browser-test dependency before adoption.

Run .NET restore, formatting verification, Release build and NUnit tests, plus
locked frontend installation, production build and non-interactive frontend tests
in CI. Backend and frontend must build independently. Keep required commands in
the existing workflow. Package command wrappers and generated helper scripts are
subject to the repository's explicit script-approval policy; scaffolding is not
blanket approval to add scripts. Review generated template files before committing.
Document terminal setup/run/stop commands and expected URLs after implementation.
No scripts, UI host, new packages, or schema changes are introduced by this ADR.

## Acceptance needed

Angular with an independent ASP.NET Core API is confirmed by the owner. The
updated transport contract, built-in host DI, three-operation journey, and
fictional prototype preceding HV-22 are presented for review. Keep the overall
ADR Proposed until these remaining boundaries are confirmed. No implementation
or scaffolding is included in this revision.

## References

- [Angular version compatibility](https://angular.dev/reference/versions)
- [Angular security and HttpClient XSRF](https://angular.dev/best-practices/security)
- [ASP.NET Core OpenAPI support](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview)
- [Blazor render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes)
- [Selective dependency injection](0003-selective-dependency-injection.md)
- [Durable local persistence](0017-durable-local-persistence.md)
- [Accepted encryption design](0018-sensitive-value-encryption.md)
