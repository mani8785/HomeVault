# Project context

HomeVault tracks anything valuable or important to a person, household, or organization. It is broader than home inventory. Examples include physical items, documents, vehicles, property, digital accounts, software licenses, insurance policies, bank accounts, and person records.

## Origin and restart

The earlier project moved away from a CRUD-first prototype toward domain-first DDD-lite. The supplied handoff reported phases 0–5 complete and phase 6 unconfirmed. Those are historical reports, not verified code in this repository. On 2026-09-23 the user requested a fresh start in D:\repos\HomeVault. The folder was empty and not a Git repository.

## Domain vocabulary

- Asset: something valuable or important that a user tracks.
- Vault: the private ownership and access boundary for assets.
- Asset attribute: flexible information owned by an asset; some values are sensitive.
- Relationship: a meaningful link between assets, such as insurance covering property.
- Evidence: a supporting reference to a file, URL, document, or note; not generally a binary blob.
- Reminder: a future action or deadline associated with an asset.

## Prior requirements to preserve

Use Guid identifiers, manual validation, lightweight domain events, and project-owned domain/result patterns. Core logic stays independent of UI, persistence, and frameworks. Encryption and storage protection belong in Infrastructure; secrets must not be casually logged or exposed. Prefer use cases and domain behavior over generic CRUD operations. Add dependencies only for a clear need.

The earlier Vault plan included Personal/Household/Organization types, Active/Archived states, and Owner/Administrator/Editor/Viewer roles. Creation supplies an initial owner; duplicate members are forbidden; the last owner cannot be removed or demoted; archived vaults cannot be modified. Vault controls member mutations. Review these rules again when implementing Vaults.

## Incremental roadmap

0. Agree on documentation and stack; scaffold the solution in a separate review step.
1. Domain language and boundaries.
2. Minimal domain building blocks.
3. Core value objects.
4. Asset registry.
5. Asset attributes.
6. Vault management.
7. Asset relationships.
8. Evidence and document links.
9. Reminders and deadlines.
10. Application use cases.
11. Repository contracts.
12. In-memory infrastructure.
13. Playground end-to-end scenarios.
14. Persistence (previous candidate: EF Core and SQL Server; reopen before adoption).
15. Authentication and authorization.
16. UI selection and implementation.

Every step requires review before the next one. A phase can contain several small review steps.
