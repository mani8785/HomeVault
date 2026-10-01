# HV-16: First application use case

HV-18 replaces this step's transient synchronous call with async repository-backed creation; see [the current implementation](hv-18-in-memory-infrastructure.md). The original HV-16 record below describes its historical scope.

Issue: [#20](https://github.com/mani8785/HomeVault/issues/20).
Contract: accepted [ADR-0012](ADRs/0012-first-application-use-case.md).

## Completed scenario

CreateVaultUseCase accepts a CreateVaultRequest containing identity, name, and
type. It obtains the initial owner from an injected ICurrentActor, read once
per call. Missing/empty identity returns Unauthenticated before request-field
validation. A null collaborator or request object is a programming error and
throws ArgumentNullException. Unexpected collaborator exceptions propagate.

Application calls Vault.Create and maps its expected failures to EmptyIdentity,
BlankName, or InvalidType. Success contains an immutable CreatedVault view with
identity, original name, type, Active status, and initial owner identity. No
mutable domain entity escapes. Default string formatting is safe; properties
are deliberate metadata reads, not redacted serialization.

Tests cover the application-to-domain flow for every Vault type, identity and
validation failures, changing actor context between calls, and safe diagnostics.
Playground manually wires a fictional actor adapter, demonstrates success and
failures, and prints no private names or actor identities.

## Boundaries

This is transient creation, not persistence. Repeated identities are not checked
for uniqueness. The identity interface does not authenticate: a future production
adapter must use verified context, never an arbitrary owner supplied by a client.
Repository contracts follow HV-17; in-memory adapters follow HV-18. Real actor
authentication and remaining use-case authorization stay deferred. This first
scenario does not complete the outstanding Asset/Relationship/Evidence/Reminder
ownership, archive, or concurrency integration work.

No packages, scripts, container, generic CRUD framework, or service locator were
introduced. Application has no Infrastructure reference.

## Terminal verification

Run each command from the repository root after the preceding succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

The no-build commands require a successful Release build. Expect application
creation True with Active state, BlankName for invalid input, and Unauthenticated
for missing identity. No result is saved after the process exits.
