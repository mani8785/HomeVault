# HV-18: In-memory Vault storage

[HV-19](hv-19-playground-scenario.md) extends this initial delivery with an explicitly shared backing store and Vault-bound Asset registration. The original default constructor still supplies an independent store.

[ADR-0014](ADRs/0014-in-memory-vault-infrastructure.md) was accepted on 2026-10-01.
CreateVaultUseCase now requires ICurrentActor and IVaultRepository and exposes
ExecuteAsync(request, cancellationToken). Existing synchronous callers must await
this method. Successful creation means the adapter accepted the Vault.

## Behavior and lifetime

InMemoryVaultRepository stores an immutable creation snapshot per identity.
Each repository instance has an independent lifetime; share one instance to
share storage. An insertion stores the Vault and sole Owner atomically under a
lock. Duplicate identities, including those from another owner, return
IdentityConflict without exposing or replacing existing metadata. Later changes
to the caller's Vault cannot affect the stored snapshot. Callers must not mutate
a supplied Vault during insertion.

The application rejects pre-cancellation before reading the actor, preserves
validation ordering, and calls storage once only after valid domain creation.
It awaits storage before returning success. Cancellation and unexpected storage
failures propagate without retries; a failed response does not imply no commit.
The adapter checks cancellation before work and again inside its insertion lock.

No disk storage, authentication provider, get/list/update API, Asset integration,
or existing-Vault authorization is implemented. Data is lost with the repository
instance/process. A missing identity is inserted; missing-record retrieval is
not a public operation under the accepted contract. Future operations remain in
the [repository checklist](ADRs/0013-vault-repository-contract.md).

## Validation and terminal steps

The new Infrastructure test project covers snapshots, all Vault types, duplicate
preservation, concurrent insertion, cancellation, invalid creation state, and
instance isolation. Its internal inspection access is for tests only. Application
tests cover identity/validation, awaited storage, conflict mapping, cancellation,
and propagated failures. Architecture tests retain the original test boundary
and enforce the new project's explicit dependencies.

Run from the repository root, proceeding only when each command succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults/hv-18
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

Playground manually shares one repository between application instances and shows
Active creation, IdentityConflict on a repeated identity, BlankName, and
Unauthenticated. Restarting the program starts with an empty store.
