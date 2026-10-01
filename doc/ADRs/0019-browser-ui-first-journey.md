# ADR-0019: Browser UI and first Vault/Asset journey

Status: Proposed
Created: 2026-10-01
Issue: [HV-23 / #27](https://github.com/mani8785/HomeVault/issues/27)

## Confirmed direction

The owner selected a browser-based application and deferred HV-21 implementation.
ADR-0018 is accepted and merged; its four implementation tasks remain parked.
Browser delivery is confirmed. The framework and prototype boundary below still
require acceptance. Hosting and authentication providers are not selected here.

## Options and recommendation

| Option | Fit for HomeVault |
| --- | --- |
| ASP.NET Core Razor Pages | Recommended for the first form-based journey: ordinary HTTP requests, server-rendered HTML, C# application calls, and no separate frontend build or API required |
| Blazor Web App, Interactive Server | Useful for richer component interactions in C#; adds persistent connection/circuit lifetime and reconnect considerations that the first three forms do not require |
| Separate browser SPA and ASP.NET Core API | Useful for independent clients; introduces API contracts, a frontend toolchain, and additional authentication/integration decisions |

Propose Razor Pages on the existing .NET 10 stack. This is a bounded choice for
the initial UI, not a claim that it fits every future interaction. Use semantic
HTML and small responsive CSS; select no component library or JavaScript framework
without a concrete need. Browser delivery does not require a SPA.

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
4. Redirect to an Asset detail URL containing its ID. InspectAssetUseCase must
   reread it on every request. Refreshing or reopening that URL proves persistence.
   Missing and inaccessible Assets share the same safe unavailable response.

Use Post/Redirect/Get after successful creation so browser refresh does not repeat
a write. Disable repeated submission for usability, but do not claim this alone
provides idempotency or exactly-once creation. Preserve form input and show safe
validation messages; unexpected failures reveal no stack traces or database paths.
Encode all user content using normal Razor output; never render stored names as
raw HTML. Do not log submitted values. Route identifiers are references, not access
credentials. Use no-store responses for pages containing user records.

The first slice does not provide Vault/Asset lists, search, editing, deletion,
membership management, attributes, files, or reminders. Existing contracts have no
general list query. Reopening a saved detail URL is supported; a future browsable
dashboard needs separately scoped actor-filtered queries and pagination.

## Project and storage boundaries

Add HomeVault.Web as the presentation/composition project after acceptance. It
calls Application use cases and wires Infrastructure adapters. Domain and
Application must not reference ASP.NET Core, UI models, or EF Core. Page models
coordinate requests and view state; domain validation remains in existing layers.

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

1. Web host and development-only composition; test environment/binding/Host guards,
   configuration failures, DI boundaries, and antiforgery rejection. Update
   architecture tests for the new host without weakening Domain isolation.
2. Vault creation, Asset registration and detail pages; reuse SQLite adapters.
   Add NUnit HTTP integration tests for success, validation, redirects, refresh,
   unauthenticated use-case failures, inaccessible/missing equivalence, Viewer
   rejection, archived writes, and current membership checks. Test actors belong
   in test composition, never an exposed identity-switching endpoint.
3. Browser verification and documentation; test actual form navigation, reload
   from SQLite after a host restart, output encoding, safe errors, keyboard flow
   and responsive layout. Review any new browser-test dependency before adoption.

Run restore, formatting verification, Release build, NUnit tests and PR CI.
Document terminal setup/run/stop commands and expected URLs after implementation.
No scripts, UI host, new packages, or schema changes are introduced by this ADR.

## Acceptance needed

Confirm Razor Pages, built-in host DI, the three-operation journey, and whether
the fictional local prototype may precede HV-22. The browser-based preference
alone does not accept these details. Keep this ADR Proposed until confirmation.

## References

- [Razor Pages architecture](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/)
- [Blazor render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes)
- [Selective dependency injection](0003-selective-dependency-injection.md)
- [Durable local persistence](0017-durable-local-persistence.md)
- [Accepted encryption design](0018-sensitive-value-encryption.md)
