# HV-19: Connected Playground journey

[ADR-0015](ADRs/0015-vault-bound-asset-registration.md) was accepted on 2026-10-01.
Playground now creates a Vault through Application and registers a fictional
bicycle in that same stored Vault. It asserts expected registration outcomes
and exits nonzero on unexpected results, rather than only printing them.

## Completed tasks

- Add Vault-bound Asset creation with immutable VaultId; preserve the original
  unbound factory for domain-only callers. Storage rejects unbound objects.
- Add RegisterAssetUseCase with trusted actor injection and an immutable view.
  Validate actor, Asset identity, Vault identity, then name before storage.
- Implement an atomic registration boundary with shared in-memory storage.
  Missing and inaccessible Vaults share VaultUnavailable. Viewers receive
  Forbidden; authorized writers to archived Vaults receive VaultArchived.
  Access checks precede global Asset identity conflicts. No write overwrites an
  existing identity or moves it between Vaults.
- Store independent creation snapshots, reject prepopulated attributes/Evidence,
  propagate cancellation and unexpected failures, and avoid automatic retries.
- Test domain compatibility, application sequencing, role/archive rules,
  isolation, concurrent duplicates, contention, snapshots, and connected flow.

The adapters must share one InMemoryHomeVaultStore. Default-constructed Vault
repositories still have private stores. Internal state seeding/inspection exists
only for Infrastructure tests and supplies no public bypass for authorization.
All future Vault membership/archive writes must use the same atomic boundary or
an equivalent reviewed transaction mechanism.

## Terminal verification

From the repository root, proceed only after each command succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/hv-19
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

After the existing domain examples, expect a connected journey with successful
Vault-bound registration, IdentityConflict, BlankName, VaultUnavailable, and
Unauthenticated, followed by `Connected journey checks passed`.

## Remaining scope and review

Storage disappears with the backing store/process; no database is chosen. The
actor is fictional, so this is not production authentication. Public get/list,
update, stored membership/archive mutations, and other Asset operations remain
future use cases in the [repository checklist](ADRs/0013-vault-repository-contract.md).
The current registration path enforces stored state; it does not implement those
future state-changing entry points. No document file upload or resolution is added.

The PR is the reviewable scenario delivery. Explicit owner review is still
required before merging or starting durable persistence selection (HV-20).
