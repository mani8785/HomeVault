# HomeVault documentation

Review [ADR-0017: Durable local persistence](ADRs/0017-durable-local-persistence.md) for the proposed SQLite/EF Core choice, migration/backup policy, and validation scope. No provider is accepted yet.

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
