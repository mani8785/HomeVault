# HV-06: Validated Asset value objects

Issue: [#10](https://github.com/mani8785/HomeVault/issues/10)

## Scope and existing constraints

This task extracts the identity and name rules already approved for
[HV-05](hv-05-domain-building-blocks.md). It does not invent Vault constraints,
new name limits, trimming, or normalization. The issue had no linked subtasks.

| Value object | Construction | Equality |
| --- | --- | --- |
| AssetId | Preserve a supplied non-empty Guid; reject Guid.Empty. | Equal exactly when the underlying Guid values are equal. |
| AssetName | Preserve a supplied nonblank string; reject null, empty, and whitespace-only text. | Exact ordinal string equality, including case, whitespace, and Unicode representation. |

Equality compares the stored value; it does not imply identity uniqueness,
case-insensitive search, or duplicate-name prevention. For example, `Bicycle`
and `bicycle` are distinct values, as are trimmed and untrimmed names. A composed
Unicode character and its decomposed representation also remain distinct.

## Implementation tasks completed

1. Add immutable sealed record classes with explicit constructors and get-only
   Value properties. Use the approved Ardalis.GuardClauses package. The reference
   types avoid a default struct instance containing an unvalidated empty Guid;
   a null reference is still absence, not a valid value object.
2. Store these values inside Asset. Keep the existing Guid Id, string Name, and
   Asset.Create(Guid, string?) interface compatible. The factory retains its
   concrete failure results and identity-first precedence; public value-object
   constructors use the documented argument-exception contract from the guards.
3. Add tests for valid/invalid construction, minimal and long nonblank names,
   preserved whitespace/Unicode, value equality, hash-based collection lookup,
   and safe diagnostic formatting. The existing Asset tests verify integration
   without changing expected behavior.

There is no generic value-object framework, implicit conversion, public setter,
or new package. Compiler-generated record equality supplies matching hash codes
and equality operators. ToString is overridden to return only AssetId or
AssetName, avoiding the default record formatting that would print Value.
Callers can read Value deliberately and must not log potentially sensitive data.

The guard implementation moved from Asset's constructor into the value-object
constructors. This preserves [ADR-0005](ADRs/0005-guard-clauses.md)'s guard policy
and result mapping; it does not change any accepted architectural decision.

## Validation and use

From the repository root, run each command only after the previous succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

There are 34 tests: 7 architecture, 11 Asset creation, and 16 value-object cases.
The unchanged Playground prints successful Asset creation, EmptyIdentity for
invalid creation, and the domain-only scope notice. The no-build commands require
a successful build. Persistence, Vault ownership/access, and additional registry
behavior remain subsequent work under their existing issues.
