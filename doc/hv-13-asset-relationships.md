# HV-13: Asset relationships — domain slice

Issue: [#17](https://github.com/mani8785/HomeVault/issues/17).
Contract and limited scope: accepted [ADR-0009](ADRs/0009-asset-relationships.md).

## Completed

Relationship is an independent root with stable Guid references, a Covers kind,
and Active/Removed state. Creation validates non-empty relationship, Vault,
source, and target identities, then kind, then rejects self-links. Failures
return safe error codes with no Relationship. Direction and references are
preserved exactly, with no loaded Asset graph or automatic reverse link.

Remove marks only the association Removed and retains every reference and kind.
Repeated removal succeeds unchanged. No endpoint is deleted and no reactivation
or editing API exists. Tests cover validation precedence, creation, removal,
retention, direction, isolation, and safe diagnostics. Playground uses fictional
references and prints only success, kind, and state.

## Remaining before the full story is complete

Application must verify endpoint existence, actual same-Vault ownership, actor
permissions, and Active Vault state for creation/removal. Active duplicate tuples
(VaultId, source, target, kind) must be rejected atomically at the storage boundary.
Reverse tuples are distinct; Removed associations do not count as active duplicates.
The current domain factory cannot enforce these global conditions, and supplied
VaultId alone is not proof of ownership. There is no category system to validate
that the source is a policy. Persistence must serialize or detect archive races.

These checks require the deferred application/repository integration. This PR
does not close HV-13. No repository, database, authorization provider, or fake
ownership lookup is introduced.

## Verify

Run each command from the repository root after the preceding succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

The no-build commands require a successful Release build. Additional output:

```text
Valid Relationship creation: True
Relationship kind: Covers; state: Active
Relationship state after removal: Removed
```
