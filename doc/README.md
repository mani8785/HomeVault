# HomeVault documentation

See accepted [ADR-0029: Authorized encrypted attributes](ADRs/0029-authorized-sensitive-attributes.md)
for #65's role policy, stable identity, additive schema and explicit host unlock.
The [encrypted attribute guide](hv-21-sensitive-attributes.md) covers #65's
implementation, explicit migration, host unlock and terminal validation.

See [ADR-0028: Windows key custody and recovery](ADRs/0028-windows-key-custody-recovery.md)
for #64's accepted secret-input interaction, protected ring/export formats and
atomic publication protocol. The [operator guide](hv-21-key-recovery.md) explains
initialization, verification and recovery. Sensitive persistence remains #65.

See [ADR-0027: Encryption envelope and write-key lifecycle](ADRs/0027-encryption-envelope-key-lifecycle.md)
for the accepted #63 framing, nonce bounds and recovery/custody contracts.
The [envelope implementation guide](hv-21-encryption-envelopes.md) covers the
internal implementation, verification and remaining production-custody boundary.
Sensitive persistence remains unavailable pending later slices.

See [ADR-0026: Durable Reminder lifecycle](ADRs/0026-durable-reminder-lifecycle.md)
for #81's accepted API, UTC storage, action privacy and concurrency contract.
The [Reminder implementation guide](hv-22-reminders.md) covers validation,
database upgrade and the terminal journey.

See [ADR-0025: Durable directed Relationships](ADRs/0025-durable-directed-relationships.md)
for #80's accepted API/schema and concurrency rules. The
[Relationship implementation guide](hv-22-relationships.md) covers behavior,
database upgrade, validation and terminal examples.

See [ADR-0024: Durable URL/Note Evidence](ADRs/0024-durable-url-note-evidence.md)
for #79's accepted API/schema and plaintext limitation. The
[Evidence implementation guide](hv-22-evidence.md) covers behavior, validation,
database upgrade and terminal examples.

See [HV-22.4.3: Ordinary attributes](hv-22-ordinary-attributes.md) for durable
add/change/remove/list operations and terminal verification under accepted
[ADR-0023](ADRs/0023-durable-ordinary-attributes.md).

See [HV-22.4.2: Authorized Vault membership](hv-22-vault-membership.md) for
add/change-role/remove operations, concurrency rules and terminal verification.

See [HV-22.4.1: Owner-only Vault archival](hv-22-vault-archive.md) for the
authenticated archive operation, transactional guarantees and terminal journey.

See [HV-22.4: Remaining authorized operations](hv-22-remaining-operations.md)
for the bounded task sequence and accepted operation/restoration decisions.

See [HV-22.3: Authorized Vault/Asset API](hv-22-authorized-api.md) for the three
authenticated operations, OpenAPI contract, permission checks and terminal guide.

See [ADR-0021: Operator invitations and recovery](ADRs/0021-operator-invitations-recovery.md)
for the accepted protected operator workflow. See [HV-22.2 account operations](hv-22-accounts.md)
for invitation-only authentication, terminal setup and validation.

See [HV-22.1: Identity storage](hv-22-identity-storage.md) for the account schema,
Windows session keys, validation scope and terminal commands.

See [ADR-0020: Local accounts and authorization](ADRs/0020-local-accounts-vault-authorization.md)
for the accepted HV-22 design. Local invitation-only authentication and Vault
authorization implementation are tracked in the linked tasks.

See [ADR-0019: Browser UI](ADRs/0019-browser-ui-first-journey.md) for the accepted
HV-23 Angular/API separation, first journey, and authentication dependency boundary.
UI implementation is parked while HV-22 authentication takes priority.

See [ADR-0018: Sensitive-value encryption](ADRs/0018-sensitive-value-encryption.md)
for the accepted HV-21 threat model, key recovery choices, and implementation test
plan. Encryption is not implemented; linked tasks track the remaining work.

See [durable persistence implementation](hv-20-persistence.md) for reviewed migration tooling and terminal validation.

See [ADR-0017: Durable local persistence](ADRs/0017-durable-local-persistence.md) for the accepted SQLite/EF Core choice, migration/backup policy, and implementation slices. Durable storage is not implemented yet.

See [HV-19: Connected Playground journey](hv-19-playground-scenario.md) for Vault-bound Asset registration, atomic access checks, and executable verification.

See [HV-18: In-memory infrastructure](hv-18-in-memory-infrastructure.md) for async application creation, adapter lifetime, isolation, and terminal verification.

See [HV-17: Repository contracts](hv-17-repository-contracts.md) for the storage-independent Vault insertion boundary and planned follow-ups.

See [HV-16: First application use case](hv-16-application-use-cases.md) for actor-bound transient Vault creation and verification.

See [HV-15: Reminder domain operations](hv-15-reminders.md) for UTC due times, lifecycle rules, and validation.

See [HV-14: Evidence domain slice](hv-14-evidence.md) for URL/note metadata, removal, and deferred file/document resolution.

See [HV-13: Relationship domain slice](hv-13-asset-relationships.md) for creation/removal and the remaining integration requirements.

See [HV-12: Vault archive](hv-12-vault-archive.md) for lifecycle behavior, blocked mutations, and remaining application enforcement.

See [HV-11: Vault membership](hv-11-vault-membership.md) for membership invariants, authorization boundaries, and verification.

See [HV-10: Vault creation](hv-10-vault-creation.md) for the initial Owner contract, Asset reference boundary, and validation commands.

See [HV-09: Sensitive attributes](hv-09-sensitive-attributes.md) for classification, deliberate reads, redaction, and verification.

See [HV-08: Asset attributes](hv-08-asset-attributes.md) for the accepted text attribute contract and terminal validation.

Start here for project context, architecture, and the decisions governing each small review step.

See [offline rotation and coordinated recovery](hv-21-rotation-recovery.md) for
#66 and accepted [ADR-0030](ADRs/0030-offline-rotation-coordinated-recovery.md).
The real Sensitive-data readiness gate requires completed verification and review.

## Table of contents

1. [Project context, domain language, and roadmap](context.md)
2. [Architecture and project dependencies](architecture.md)
3. [Workflow, review gates, testing, and XML documentation](workflow.md)
4. [References and further reading](references.md)
5. [Architecture decision process and index](ADRs/README.md)
6. [ADR-0001: Initial language, runtime, and testing stack](ADRs/0001-initial-stack.md)
7. [ADR-0002: Domain-first DDD-lite and project boundaries](ADRs/0002-domain-first-boundaries.md)
8. [ADR-0003: Selective dependency injection](ADRs/0003-selective-dependency-injection.md)

9. [ADR-0004: Domain language and aggregate boundaries (Accepted)](ADRs/0004-domain-language-and-boundaries.md)

See also [Phase 0 Git, CI/CD, and review policy](ci-cd.md). ADR-0001 and ADR-0002 were accepted on 2026-09-25, and HV-03 scaffolding was authorized. ADR-0003 preserves the accepted dependency injection direction. The solution and architecture tests implement this foundation; domain use cases remain subsequent reviewed steps.

See the [HV-05 implementation record](hv-05-domain-building-blocks.md) for the first Asset creation operation, validation behavior, and deferred capabilities.

See [HV-06: Validated Asset value objects](hv-06-value-objects.md) for AssetId and AssetName construction, equality, and compatibility guarantees.

See [HV-07: Register and inspect an Asset](hv-07-register-inspect.md) for the initial supported scenario, acceptance coverage, and current Playground output.
