# ADR-0031: Authorized library browsing for the Angular workspace

Status: Accepted
Created: 2026-10-10
Accepted: 2026-10-10 (explicit owner confirmation in the task)
Related: [HV-23](https://github.com/mani8785/HomeVault/issues/27)

## Context

The owner requested a Zotero-inspired three-pane workspace and UI coverage of
implemented operations. Existing HTTP endpoints operate on known identities;
there is no browsable Vault/Asset collection or member/Relationship/Reminder
listing. Local browser history cannot serve as an authoritative library.

## Proposed decision

Add explicit read-only Application query contracts implemented with the existing
EF Core/SQLite adapter and exposed through versioned authenticated API routes.
Do not add generic CRUD repositories, a new ORM, a search engine or schema changes.
Use no-tracking projections and filter access in the database, before pagination.
Every request uses the current authenticated actor and current Vault membership.

| Query | Visible metadata and access |
| --- | --- |
| GET /api/v1/vaults | Only the actor's Vaults: id, name, type, status, current role |
| GET /api/v1/vaults/{id}/assets | Asset id, Vault id and name for current Vault members |
| GET /api/v1/vaults/{id}/members | Actor id and role for Owner/Administrator only; no global account directory or email disclosure |
| GET /api/v1/vaults/{id}/relationships | Current Vault members; id, endpoints, kind and lifecycle status; optional Asset filter |
| GET /api/v1/vaults/{id}/reminders | Current Vault members; id, Asset id, due instant and status; optional Asset filter; no action text |

Each collection uses a deterministic identity ordering and bounded offset paging:
default 50, maximum 100 records; reject invalid/negative offsets and invalid page
sizes. Return items and whether a next page exists. Offset paging is not a
snapshot: concurrent changes can shift pages, and Refresh starts at the beginning.
Do not expose total counts for inaccessible data. Filter visible names only for
Vault/Asset search (bounded 200-character query); never search private values.
Access checks and collection projection share a read transaction where separate
statements are required. Missing and inaccessible Vaults return the same 404.
Archived Vaults remain readable; their accepted write restrictions remain intact.

UI role hints hide unavailable commands for usability; existing mutation endpoints
must still recheck authorization and archival state. A 401/403/404 clears stale
selection/private content. No record payloads in localStorage, IndexedDB or service
workers. Sensitive values, Evidence content and Reminder action text are fetched
only by explicit user actions and cleared on selection changes and sign-out.

## Alternatives

Manual IDs/bookmarks can expose existing operations but do not provide a library.
Client-maintained lists omit existing records and become stale after access changes.
Loading entire tables and filtering in Angular would disclose unauthorized data.
Cursor pagination scales better for large collections; bounded offset pages keep
the first local library contract small, with the consistency limitation explicit.

## Validation handoff

Add query/API tests for anonymous use, cross-Vault isolation, role changes,
archived reads, deterministic pages, invalid paging, metadata-only results and
missing/inaccessible equivalence. The owner runs tests and reviews CI/PRs under
the updated skill preference. The owner explicitly accepted this query/access
contract and authorized implementation.
