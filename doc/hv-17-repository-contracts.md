# HV-17: Repository contract for Vault creation

HV-18 now implements this contract and integrates it with Application; see [the current implementation](hv-18-in-memory-infrastructure.md). This record preserves the HV-17 contract-only delivery scope.

[ADR-0013](ADRs/0013-vault-repository-contract.md) was accepted on 2026-10-01.
Application now owns `IVaultRepository.AddAsync` and `VaultAddOutcome`, using
only Domain and base-library types. Infrastructure will implement the contract
in HV-18; storage technology remains undecided.

## Completed scope

- Derived one insertion operation from the existing CreateVaultUseCase.
- Specified atomic insertion, identity conflict without overwrite, initial
  ownership, snapshot isolation, cancellation, and storage failure behavior in
  public XML documentation.
- Kept Domain and the existing transient use case unchanged. There is no adapter
  yet, so neither storage behavior nor concurrent insertion is implemented here.
- Recorded get-by-id, bounded listing, update concurrency, Asset storage,
  archive behavior, and adapter contract tests in the ADR follow-up checklist.
  Existing backlog coverage is partial; those scenarios require dedicated
  refinement rather than assuming HV-18 implements unused CRUD operations.

Missing-record reads are intentionally absent: insertion creates a missing
identity and rejects an existing one. Application must bind the sole initial
Owner to its trusted actor; the repository does not authenticate callers.
An adapter must store the complete Vault independently of the caller's mutable
instance and must make duplicate detection atomic, rather than checking then
inserting without synchronization.

## Verification

Run from the repository root, proceeding only after each command succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/hv-17
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

Existing behavioral and architecture tests remain applicable. No test that merely
mirrors the interface declaration is added: concurrency, duplicate preservation,
cancellation, and snapshot guarantees must be tested against the HV-18 adapter.
Playground still demonstrates transient creation with Active status, BlankName,
and Unauthenticated outcomes. It does not demonstrate persistence.
