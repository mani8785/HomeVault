# HV-08: Maintain flexible Asset attributes

Issue: [#12](https://github.com/mani8785/HomeVault/issues/12).
Contract: accepted [ADR-0006](ADRs/0006-asset-attributes.md).

## Completed tasks

1. Agree the text-only contract, name matching, validation precedence, and mutation rules.
2. Add immutable AssetAttribute entries and safe AssetAttributeError outcomes.
3. Implement Asset.AddAttribute, ChangeAttribute, RemoveAttribute, and immutable
   snapshots through Attributes. Names are trimmed and matched ignoring ordinal
   case; values are preserved exactly. Change preserves the original name spelling.
4. Cover success, validation, duplicates, missing entries, failure atomicity,
   snapshot isolation, per-Asset isolation, and safe diagnostics with NUnit tests.
5. Extend the fictional Playground example to add, change, inspect, and remove
   Material and demonstrate DuplicateName.

None means success. BlankName, BlankValue, DuplicateName, and NotFound are safe
failure outcomes; callers should inspect these rather than log supplied text.
Attribute values may be sensitive. Neither entry ToString nor error formatting
prints a supplied name or value. Property access deliberately exposes the text.

This is domain-only behavior. Typed values, sensitivity policy, Vault access,
persistence, and encryption remain separate backlog work. No new dependencies,
scripts, generic bases, or application services were added.

## Verify from the repository root

Run each command only after the preceding command succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

The no-build commands require a successful Release build. Alongside the existing
creation output, expect Add attribute: None, Duplicate attribute: DuplicateName,
Change attribute: None, Example attribute: Material = Aluminium, and
Remove attribute: None. Only fictional data is printed. Nothing is persisted.
