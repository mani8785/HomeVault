# HV-07: Register and inspect an Asset

The original domain-only scope below is extended by [HV-19](hv-19-playground-scenario.md), which registers Vault-bound Assets through Application and shared in-memory storage.

Issue: [#11](https://github.com/mani8785/HomeVault/issues/11)

## Scope and completed tasks

The initial scenario uses the minimum information already approved in
[HV-05](hv-05-domain-building-blocks.md): a caller-supplied non-empty Guid and
a nonblank name. A bicycle or insurance policy can be represented by these
fields; category-specific information follows under HV-08. Valid names are
preserved exactly, including surrounding whitespace and Unicode.

This story has no linked subtasks. Its three acceptance criteria are covered by:

1. Reuse the approved minimum contract and the validated values from
   [HV-06](hv-06-value-objects.md).
2. Reuse `Asset.Create`: success returns an Asset; invalid input returns
   `EmptyIdentity` or `BlankName` with no Asset. Identity validation takes
   precedence when both inputs are invalid.
3. Inspect the resulting `Id` and `Name` through the existing
   `ValidCreationPreservesCallerIdentityAndName` NUnit cases. The Playground
   now displays both properties for fictional example data and demonstrates
   both validation errors.

The existing 11 Asset creation cases cover the story's success, inspection,
failure, precedence, and safe-diagnostic behavior. The 16 value-object cases
and 7 architecture cases also remain relevant. No duplicate factory, service,
or test suite is needed to complete this domain scenario.

## Boundaries

Registration here means creating and inspecting a domain object in the current
process. Nothing is saved or retrievable after the process exits. Vault ownership
and authorization remain HV-10/HV-22; application orchestration and repositories
remain HV-16/HV-17. Global identity uniqueness is not checked. This follows
[ADR-0004](ADRs/0004-domain-language-and-boundaries.md)'s early domain-test scope
and does not change its requirement that a persisted Asset belongs to one Vault.

The console deliberately displays fictional data. Real Asset names must not be
copied into diagnostic logs. Validation output uses error codes only.

## Terminal validation

From the repository root, run each command after the previous succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

The `--no-build` commands require a successful Release build. Expected output:

```text
Valid Asset creation: True
Asset identity: 74128a99-4eb5-4b75-8ad1-6bf2d2c8453d
Asset name: Example bicycle
Invalid Asset creation: EmptyIdentity
Blank Asset name: BlankName
Domain-only demonstration: nothing is persisted and Vault access is not implemented.
```
