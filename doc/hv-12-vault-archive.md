# HV-12: Archive a Vault

Issue: [#16](https://github.com/mani8785/HomeVault/issues/16).

The transitions and strict archive rules were explicitly accepted in
[ADR-0004](ADRs/0004-domain-language-and-boundaries.md): an Owner may archive,
repeated archive succeeds after access checks, permitted reads remain available,
and all mutations are prohibited. Reactivation and deletion are out of scope.
The issue has no linked subtasks and requires no new architectural decision.

## Completed domain tasks

1. Add Vault.Archive(requestingActorId). Validate a non-empty identity, then
   require a current Owner membership, even when already Archived. Return safe
   EmptyActorIdentity or NotOwner failures. Non-members and non-Owner roles
   receive the same NotOwner result. Success returns None.
2. Preserve the Vault identity, name, type, and memberships for reads. Archive
   changes only Status from Active to Archived. Repeated Owner requests succeed.
3. Reject AddMember, ChangeMemberRole, and RemoveMember on Archived Vaults with
   Archived before input validation, lookup, or same-role no-op handling. This
   includes removal of members: strict archival does not allow access revocation.
4. Test all Vault types, every non-Owner role, missing/empty actors, changed
   ownership, repeated archival, blocked mutations, retained reads, and isolation.
5. Demonstrate archive and blocked add-member in the fictional Playground.

## Integration boundary and remaining enforcement

Application must authenticate the requesting actor and supply its identity;
passing an Owner's Guid to the domain method is not authentication. Application
also gates reads by Vault access. Domain properties do not enforce read access.

This implementation protects all currently available Vault mutations. Assets
still have no VaultId or application registration boundary, as agreed in
[ADR-0008](ADRs/0008-vault-creation.md). Their standalone attribute operations
cannot consult a Vault. Therefore archive enforcement for Asset writes, future
Evidence/Relationships/Reminders, and concurrent writes remains integration
work for the application and persistence slices. It is not implemented or
claimed here. Those entry points must reject Archived writes, with serialization
or conflict detection as required by ADR-0004. Domain instances are not thread-safe.

## Terminal validation

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
Archive Vault: None
Vault state after archive: Archived
Add member after archive: Archived
```

No scripts, packages, persistence, or authentication provider were introduced.
