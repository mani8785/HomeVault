# HV-10: Create a Vault with an initial Owner

Issue: [#14](https://github.com/mani8785/HomeVault/issues/14).
Contract: accepted [ADR-0008](ADRs/0008-vault-creation.md).

## Completed tasks

1. Define the creation contract while preserving the accepted Personal,
   Household, and Organization contexts and role vocabulary.
2. Implement Vault.Create with a caller-supplied non-empty Guid, nonblank name,
   supported type, and non-empty initial actor identity. Preserve names exactly.
3. Return an Active Vault with exactly one immutable Owner membership, or a
   concrete safe error and no Vault. Validate id, name, type, then owner identity.
4. Test all contexts, naming, invalid inputs, validation precedence, ownership,
   protected membership inspection, and safe diagnostics. Demonstrate successful
   creation and EmptyOwnerIdentity in Playground without printing Vault names
   or actor identifiers.

## Asset reference boundary

A persisted Asset references one Vault through VaultId. Vault contains no Asset
collection and creation never loads Assets. Registration integration will require
that identity and verify existence, lifecycle, and access in Application. The
standalone Asset factory remains domain-only until that integration is implemented.
Creating a Vault here does not persist it, authenticate its owner, establish an
authorized private store, or attach existing Assets. Membership changes and
archival operations follow HV-11 and HV-12 respectively.

## Terminal validation

From the repository root, run each command after the preceding succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

The no-build commands require a successful Release build. Expect these additional
lines alongside the existing Asset scenario:

```text
Valid Vault creation: True
Vault state: Active; initial role: Owner
Invalid Vault creation: EmptyOwnerIdentity
```

No new dependencies, generic DDD bases, or scripts were introduced.
