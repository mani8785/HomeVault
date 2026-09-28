# HV-05: Minimal domain building blocks

Issue: [#9](https://github.com/mani8785/HomeVault/issues/9)

## Approved scope

On 2026-09-28, the owner selected minimal Asset creation: a non-empty Guid,
a nonblank name, and useful validation failures. The owner also explicitly
accepted all four decision items in [ADR-0004](ADRs/0004-domain-language-and-boundaries.md).
No separate subtasks existed; implementation follows the three tasks below.

## Completed tasks

1. Implement `Asset.Create(Guid, string?)` and immutable inspection of its Id
   and Name. Validate with Ardalis.GuardClauses, checking identity first. Preserve valid names
   exactly; do not introduce trimming, normalization, uniqueness, or length rules.
2. Return a concrete `AssetCreationResult`: success contains an Asset and
   `None`; failure contains no Asset and `EmptyIdentity` or `BlankName`.
   Validation failures do not throw or include caller-supplied values. A caller
   inspects `IsSuccess`, then the nullable `Asset` or the error code.
3. Add NUnit coverage for valid values, preserved names, empty identity,
   null/empty/whitespace names, validation precedence, and safe failure diagnostics.
   Document public APIs with XML comments and demonstrate both outcomes in Playground.

The result is specific to this operation. There is no generic Entity,
AggregateRoot, Result<T>, event base class, or dispatch infrastructure. No event
is currently required: there is no consumer, persistence commit, or downstream
reaction. When an approved use case needs an event, express it as plain C# with
an explicitly defined emission point instead of adding speculative machinery.

## Boundary and follow-up work

Creation is domain-only. It does not persist, validate global identity uniqueness,
look up Vaults, authorize access, or add attributes. A persisted Asset will still
require the agreed Vault ownership checks. Asset and result default string
representations do not format the name; callers must also avoid logging names
or other potentially sensitive values themselves.

HV-06 (#10) still needs specific value-object constraints and equality semantics.
This slice implements the creation core relevant to HV-07 (#11), but does not
close that story or imply that its full supported scenarios and integration are
complete. Later work should reuse this operation and refine its contract rather
than introduce duplicate types. Vault behavior and all other later capabilities
remain outside this change.

## Run and verify

From the repository root, run each command after the previous succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

Expected Playground output:

```text
Valid Asset creation: True
Invalid Asset creation: EmptyIdentity
Domain-only demonstration: nothing is persisted and Vault access is not implemented.
```

The `--no-build` commands require a successful build first. Tests include the
existing architecture suite and the new Asset creation cases. Ardalis.GuardClauses is the approved input-guard dependency; no scripts are needed.

The owner requested the guard library during PR review on 2026-09-28. [ADR-0005](ADRs/0005-guard-clauses.md) records the approved dependency exception and safe exception-to-result mapping. Public behavior is unchanged.
