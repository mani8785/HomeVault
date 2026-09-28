# HV-11: Manage Vault membership and roles

Issue: [#15](https://github.com/mani8785/HomeVault/issues/15).

The Owner/Administrator/Editor/Viewer capabilities, one-membership-per-actor
rule, and last-Owner protection were explicitly accepted in
[ADR-0004](ADRs/0004-domain-language-and-boundaries.md). HV-11 implements these
local membership invariants; no new architectural decision or reconfirmation
of the accepted role matrix is required. The issue has no linked subtasks.

## Completed tasks

1. Add Vault.AddMember, ChangeMemberRole, and RemoveMember. Use approved guards
   for non-empty actor identities and supported roles; return safe error codes.
2. Reject duplicate members and missing change/remove targets. Reject removal
   or demotion of the last Owner. Adding/promoting another Owner permits removal
   or demotion of the original Owner. A same-role change succeeds unchanged.
3. Expose immutable membership snapshots; old snapshots retain their earlier
   roles after mutations. Failed operations leave every membership unchanged.
4. Test all roles, all Vault types, failures and precedence, ownership transfer,
   multiple-owner removal, snapshot protection, and per-Vault isolation.
5. Extend Playground with fictional add/change/remove and LastOwner rejection.

Validation checks actor identity, then role where applicable, then membership
existence/duplication, then last-Owner protection. Outcomes are None,
EmptyActorIdentity, InvalidRole, DuplicateMember, MemberNotFound, and LastOwner.
No error contains actor identifiers or other supplied values.

## Authorization and lifecycle boundary

These methods receive the target member identity, not the requesting actor.
They do not authenticate or authorize a request. HV-22 must enforce the accepted
matrix: Owners can manage all roles while preserving an Owner; Administrators
can manage only Editors/Viewers and cannot promote themselves or modify an
Administrator/Owner; Editors/Viewers cannot manage membership. Do not expose
these domain operations as an unprotected application API.

The Vault currently remains Active. The accepted strict archive rule will be
enforced when HV-12 introduces the archival transition; no archival API is added
here. Persistence and concurrency checks remain later work. Instances are not
thread-safe; the future application/persistence boundary must serialize writes
or detect conflicts as required by ADR-0004.

## Terminal validation

Run each command from the repository root after the preceding succeeds:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore -warnaserror
dotnet test --configuration Release --no-build --no-restore --logger trx --results-directory TestResults
dotnet run --project src/HomeVault.Playground --configuration Release --no-build
```

The no-build commands require a successful Release build. Playground prints
None for add/change/remove, then LastOwner for attempted final Owner removal.
No actor IDs are displayed. No packages, scripts, or generic frameworks were added.
